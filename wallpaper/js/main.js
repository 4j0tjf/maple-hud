/*
 * 동기화 흐름
 *  1) 캐릭터 결정: 설정에 이름이 있으면 그 캐릭터들, 없으면 계정 캐릭터 목록(character/list)에서
 *     인게임 스케줄러에 항목이 등록된 캐릭터를 찾는다.
 *  2) 캐릭터마다 scheduler/character-state (+ 아바타용 character/basic) 조회
 *  3) 응답은 localStorage에 캐시해서 배경화면이 다시 켜져도 바로 보이고, 호출량도 아낀다.
 */
(function () {
  'use strict';

  var MH = window.MH;
  var time = MH.time;
  var store = MH.store;
  var scheduler = MH.scheduler;
  var config = MH.config;
  var hud = MH.hud;

  var MIN = 60 * 1000;
  var HOUR = 60 * MIN;
  var TTL = {
    list: 12 * HOUR,      // 계정 캐릭터 목록
    ocid: 30 * 24 * HOUR, // 캐릭터명 → ocid
    basic: 6 * HOUR,      // 아바타/경험치
    discovery: 6 * HOUR   // 스케줄러 등록 여부 탐색 결과
  };
  var MAX_SCAN = 40;
  var MANUAL_REFRESH_GAP = 30 * 1000;
  // 이 오류들은 모든 캐릭터에 똑같이 실패하므로 동기화를 멈추고 전체 알림으로 띄운다
  var FATAL = ['OPENAPI00005', 'OPENAPI00006', 'OPENAPI00007', 'OPENAPI00010', 'OPENAPI00011', 'NETWORK'];

  var state = {
    chars: [],
    notice: null,
    syncing: false,
    lastSync: 0,
    demo: false,
    paused: false
  };
  var syncTimer = null;
  var pending = null;
  var lastManual = 0;
  var lastDailyReset = time.lastReset(time.RESET.DAILY, Date.now());

  function isFatal(e) {
    return e && FATAL.indexOf(e.code) >= 0;
  }

  function errText(e) {
    if (!e) return '알 수 없는 오류';
    if (e.code === 'OPENAPI00002') return '조회 권한이 없습니다 (API 키를 만든 계정의 캐릭터인지 확인)';
    return e.message || String(e);
  }

  function refreshMs(cfg) {
    return Math.max(5, Number(cfg.refreshMin) || 15) * MIN;
  }

  function parseNames(text) {
    var seen = {};
    return String(text || '').split(/[,\n;]/).map(function (s) { return s.trim(); }).filter(function (s) {
      if (!s || seen[s]) return false;
      seen[s] = true;
      return true;
    });
  }

  // 캐릭터 구성이 바뀌는 설정(키 끝자리, 이름 목록, 레벨 조건)
  function targetsSig(cfg) {
    return [String(cfg.apiKey).slice(-8), cfg.baseUrl, cfg.characters, cfg.minLevel, cfg.maxChars].join('|');
  }

  function flattenList(body) {
    var out = [];
    ((body && body.account_list) || []).forEach(function (acc) {
      (acc.character_list || []).forEach(function (c) {
        out.push({
          name: c.character_name,
          ocid: c.ocid,
          world: c.world_name,
          cls: c.character_class,
          level: Number(c.character_level) || 0
        });
      });
    });
    return out;
  }

  /* ---------- 캐시를 거치는 API 호출 ---------- */

  function cached(key, ttl, force, fetcher) {
    var hit = !force && store.getCached(key, ttl);
    if (hit) return Promise.resolve(hit);
    return fetcher().then(function (data) { return store.setCached(key, data); });
  }

  function schedulerEntry(client, ocid, force, cfg) {
    // 주기보다 살짝 짧게 잡아야 다음 주기에 새로 받아온다
    return cached('sched:' + ocid, refreshMs(cfg) - 30 * 1000, force, function () {
      return client.scheduler(ocid);
    });
  }

  /* ---------- 캐릭터 결정 ---------- */

  function resolveByNames(client, names) {
    var listEntry = store.getCached('charlist', null);
    var known = {};
    flattenList(listEntry && listEntry.data).forEach(function (c) { known[c.name] = c; });

    var out = [];
    return names.reduce(function (p, name) {
      return p.then(function () {
        if (known[name]) {
          out.push(known[name]);
          return;
        }
        return cached('ocid:' + name, TTL.ocid, false, function () { return client.ocid(name); })
          .then(function (entry) {
            out.push({ name: name, ocid: entry.data });
          }, function (e) {
            if (isFatal(e)) throw e;
            out.push({ name: name, ocid: null, error: e.code === 'OPENAPI00004' ? '캐릭터를 찾을 수 없습니다' : errText(e) });
          });
      });
    }, Promise.resolve()).then(function () { return out; });
  }

  function resolveFromAccount(client, cfg) {
    return cached('charlist', TTL.list, false, function () { return client.characterList(); }).then(function (entry) {
      var all = flattenList(entry.data)
        .filter(function (c) { return c.level >= (Number(cfg.minLevel) || 0); })
        .sort(function (a, b) { return b.level - a.level; })
        .slice(0, MAX_SCAN);
      if (!all.length) {
        state.notice = { level: 'warn', text: '레벨 ' + cfg.minLevel + ' 이상 캐릭터가 계정에 없습니다. 최소 레벨 설정을 확인하세요.' };
        return [];
      }

      var maxChars = Math.max(1, Number(cfg.maxChars) || 8);
      var found = [];
      // 스케줄러에 항목이 등록된 캐릭터를 레벨 순으로 찾는다. 결과는 TTL.discovery 동안 재사용
      return all.reduce(function (p, c) {
        return p.then(function () {
          if (found.length >= maxChars) return;
          var reg = store.getCached('reg:' + c.ocid, TTL.discovery);
          if (reg) {
            if (reg.data) found.push(c);
            return;
          }
          return schedulerEntry(client, c.ocid, false, cfg).then(function (sched) {
            var registered = scheduler.hasRegistered(sched.data);
            store.setCached('reg:' + c.ocid, registered);
            if (registered) found.push(c);
          }, function (e) {
            if (isFatal(e)) throw e;
          });
        });
      }, Promise.resolve()).then(function () {
        if (found.length) return found;
        state.notice = { level: 'info', text: '인게임 스케줄러에 등록된 캐릭터가 없어 레벨 순으로 표시합니다.' };
        return all.slice(0, maxChars);
      });
    });
  }

  // 이전 동기화 결과/캐시를 이어받아 로딩 중에도 화면이 비지 않게 한다
  function adopt(targets) {
    var prev = {};
    state.chars.forEach(function (c) { prev[c.ocid || c.name] = c; });
    return targets.map(function (t) {
      var old = prev[t.ocid || t.name] || {};
      var c = {
        name: t.name,
        ocid: t.ocid,
        world: t.world || old.world,
        cls: t.cls || old.cls,
        level: t.level || old.level,
        body: old.body || null,
        fetchedAt: old.fetchedAt || 0,
        basic: old.basic || null,
        error: t.error || null,
        loading: false
      };
      if (c.ocid && !c.body) {
        var s = store.getCached('sched:' + c.ocid, null);
        if (s) {
          c.body = s.data;
          c.fetchedAt = s.at;
        }
      }
      if (c.ocid && !c.basic) {
        var b = store.getCached('basic:' + c.ocid, null);
        if (b) c.basic = b.data;
      }
      return c;
    });
  }

  function loadChar(client, c, cfg, force) {
    if (!c.ocid) return Promise.resolve();
    c.loading = true;
    render();
    return schedulerEntry(client, c.ocid, force, cfg).then(function (entry) {
      c.body = entry.data;
      c.fetchedAt = entry.at;
      c.error = null;
      store.setCached('reg:' + c.ocid, scheduler.hasRegistered(entry.data));
      if (!cfg.showAvatar) return;
      return cached('basic:' + c.ocid, TTL.basic, false, function () { return client.basic(c.ocid); })
        .then(function (b) { c.basic = b.data; }, function (e) { if (isFatal(e)) throw e; });
    }).catch(function (e) {
      if (isFatal(e)) throw e;
      if (e.code === 'OPENAPI00003') store.remove('ocid:' + c.name);
      c.error = errText(e);
    }).finally(function () {
      c.loading = false;
      render();
    });
  }

  /* ---------- 동기화 ---------- */

  function loadDemo() {
    state.demo = true;
    state.notice = null;
    var now = Date.now();
    state.chars = MH.demo.build().map(function (c) {
      c.fetchedAt = now - 4 * MIN;
      return c;
    });
    state.lastSync = now;
  }

  function sync(force) {
    if (state.syncing) {
      pending = { force: force || (pending && pending.force) };
      return;
    }
    clearTimeout(syncTimer);
    var cfg = config.get();

    if (cfg.demo || (!cfg.apiKey && !cfg.baseUrl)) {
      loadDemo();
      render();
      return;
    }

    if (state.demo) state.chars = [];
    state.demo = false;
    state.syncing = true;
    state.notice = null;
    if (!state.chars.length) {
      // 배경화면이 다시 켜졌을 때 지난번 캐릭터와 캐시를 먼저 보여준다 (오프라인이어도)
      var last = store.get('targets');
      if (last && last.sig === targetsSig(cfg)) state.chars = adopt(last.list || []);
    }
    render();

    var client = MH.api.create({ apiKey: cfg.apiKey, baseUrl: cfg.baseUrl });
    var names = parseNames(cfg.characters);
    var resolving = names.length ? resolveByNames(client, names) : resolveFromAccount(client, cfg);

    resolving.then(function (targets) {
      state.chars = adopt(targets);
      store.set('targets', { sig: targetsSig(cfg), list: targets });
      render();
      return state.chars.reduce(function (p, c) {
        return p.then(function () { return loadChar(client, c, cfg, force); });
      }, Promise.resolve());
    }).then(function () {
      state.lastSync = Date.now();
    }).catch(function (e) {
      state.notice = { level: 'error', text: errText(e) };
    }).finally(function () {
      state.syncing = false;
      scheduleNext();
      render();
      if (pending) {
        var next = pending;
        pending = null;
        sync(next.force);
      }
    });
  }

  function scheduleNext() {
    clearTimeout(syncTimer);
    var cfg = config.get();
    var now = Date.now();
    // 정기 갱신 + 자정 초기화 후 API 반영 시점에 한 번 더
    var afterReset = time.nextReset(time.RESET.DAILY, now) + scheduler.DATA_DELAY_MS + MIN;
    var at = Math.min(now + refreshMs(cfg), afterReset);
    syncTimer = setTimeout(function () {
      // 보이지 않는 동안에는 호출하지 않고, 다시 보일 때 resume()이 처리한다
      if (isIdle()) return;
      sync(false);
    }, Math.max(MIN, at - now));
  }

  // WE가 배경화면을 멈췄거나(전체 화면 게임 등) 창이 가려져 페이지가 숨겨진 상태
  function isIdle() {
    return state.paused || document.hidden;
  }

  function resume() {
    if (isIdle()) return;
    hud.tick(statusView(), Date.now());
    if (state.syncing) return;
    if (Date.now() - state.lastSync > refreshMs(config.get())) sync(false);
    else scheduleNext();
  }

  /* ---------- 화면 ---------- */

  function collapsedPref(key) {
    var pref = store.get('collapse:' + key);
    if (!pref) return null;
    // 접은 상태는 유지, 펼친 상태는 그날만 유효 (다음 날엔 자동 접기 규칙을 다시 따른다)
    if (pref.v === false && pref.day !== time.lastReset(time.RESET.DAILY, Date.now())) return null;
    return pref.v;
  }

  function toCards(cfg) {
    var now = Date.now();
    return state.chars.map(function (c) {
      var model = c.body ? scheduler.normalize(c.body, c.fetchedAt || 0, now, { showAll: cfg.showAll }) : null;
      var basic = c.basic || {};
      var key = c.ocid || c.name;
      var complete = model && model.count.all.total > 0 && model.count.all.done === model.count.all.total;
      var pref = collapsedPref(key);
      return {
        key: key,
        name: (model && model.name) || basic.character_name || c.name,
        level: basic.character_level || (model && model.level) || c.level,
        cls: basic.character_class || (model && model.cls) || c.cls,
        world: basic.world_name || (model && model.world) || c.world,
        exp: basic.character_exp_rate ? Number(basic.character_exp_rate).toFixed(2) : '',
        image: cfg.showAvatar ? basic.character_image : '',
        model: model,
        loading: c.loading,
        error: c.error,
        collapsed: pref != null ? pref : !!(cfg.collapseDone && complete)
      };
    });
  }

  function statusView() {
    return {
      showSeconds: config.get().showSeconds,
      notice: state.notice,
      demo: state.demo,
      syncing: state.syncing,
      lastSync: state.lastSync,
      isWE: config.isWE()
    };
  }

  function view() {
    var v = statusView();
    v.cards = toCards(config.get());
    return v;
  }

  var renderQueued = false;
  function render() {
    if (renderQueued) return;
    renderQueued = true;
    requestAnimationFrame(function () {
      renderQueued = false;
      hud.render(view(), config.get());
    });
  }

  function toggleCard(key) {
    var card = toCards(config.get()).filter(function (c) { return c.key === key; })[0];
    if (!card) return;
    store.set('collapse:' + key, { v: !card.collapsed, day: time.lastReset(time.RESET.DAILY, Date.now()) });
    render();
  }

  function manualRefresh() {
    var now = Date.now();
    if (state.syncing || now - lastManual < MANUAL_REFRESH_GAP) return;
    lastManual = now;
    sync(true);
  }

  /* ---------- 시작 ---------- */

  var changeTimer = null;

  function onConfigChange(keys) {
    var cfg = config.get();
    hud.applyStyle(cfg);
    render();
    var dataChanged = keys.some(function (k) { return config.dataKeys.indexOf(k) >= 0; });
    if (keys.indexOf('refreshMin') >= 0 && !state.syncing) scheduleNext();
    if (!dataChanged) return;
    // WE에서 API 키를 타이핑하는 동안 매번 호출하지 않도록 잠시 기다린다
    clearTimeout(changeTimer);
    changeTimer = setTimeout(function () {
      if (keys.indexOf('apiKey') >= 0 || keys.indexOf('baseUrl') >= 0) state.chars = [];
      sync(false);
    }, config.isWE() ? 1500 : 200);
  }

  function start() {
    var started = false;
    function first() {
      if (started) return;
      started = true;
      config.onChange(onConfigChange);
      hud.applyStyle(config.get());
      sync(false);
    }
    // WE는 로드 직후 속성 전체를 한 번 보내준다. 그 전에 데모/빈 키로 호출하지 않도록 기다린다
    config.onWEReady(first);
    setTimeout(first, config.isWE() ? 2500 : 400);
  }

  function init() {
    // 한 달 넘게 안 쓴 캐시(지금은 표시하지 않는 캐릭터 등)를 정리한다
    store.prune(31 * 24 * HOUR);
    if (!config.isWE()) config.loadBrowserSettings();
    config.loadUrlParams();

    var overlay = window.mapleOverlay;
    MH.settings.init({
      // 오버레이 앱 창은 평소엔 포커스를 받지 않으므로, 설정창을 여는 동안만 키보드 입력을 받게 한다
      onOpen: function () { if (overlay) overlay.setFocusable(true); },
      onClose: function () { if (overlay) overlay.setFocusable(false); }
    });
    hud.init({ onToggle: toggleCard, onRefresh: manualRefresh, onSettings: MH.settings.open });
    hud.applyStyle(config.get());
    render();

    config.onPause(function (paused) {
      state.paused = paused;
      hud.setPaused(paused);
      resume();
    });
    document.addEventListener('visibilitychange', resume);

    window.addEventListener('resize', function () { hud.fitHeight(config.get()); });

    setInterval(function () {
      if (isIdle()) return;
      var now = Date.now();
      var reset = time.lastReset(time.RESET.DAILY, now);
      if (reset !== lastDailyReset) {
        // 자정이 지나면 완료 표시를 미리 해제해서 다시 그린다
        lastDailyReset = reset;
        render();
      } else {
        hud.tick(statusView(), now);
      }
    }, 1000);

    start();
  }

  MH.app = { refresh: manualRefresh, openSettings: function () { MH.settings.open(); } };
  init();
})();
