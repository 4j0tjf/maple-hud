/*
 * /maplestory/v1/scheduler/character-state 응답을 HUD용 모델로 정리한다.
 * - 인게임 스케줄러에 등록된(registration_flag) 항목만 보여주는 것이 기본
 * - 캐릭터마다 사용자가 고른 표시 설정(selection)이 있으면 그걸 먼저 따른다
 * - 데이터를 받은 뒤 초기화 시각이 지났다면 완료 표시를 미리 해제한다 (API 반영 지연 대비)
 */
(function (root) {
  'use strict';

  var time = root.MH && root.MH.time;
  if (!time && typeof require === 'function') time = require('./time.js');
  var RESET = time.RESET;
  // 초기화 직후 API는 한동안(평균 15분) 이전 데이터를 줄 수 있다
  var DATA_DELAY_MS = 20 * 60 * 1000;

  // 표시 설정 화면의 분류 (이 순서로 보여준다)
  var GROUPS = [
    { id: 'daily', label: '일일' },
    { id: 'weekly', label: '주간' },
    { id: 'bossWeekly', label: '주간 보스' },
    { id: 'bossMonthly', label: '월간 보스' },
    { id: 'bossDaily', label: '일일 보스' }
  ];

  function isTrue(v) {
    return v === true || v === 1 || v === 'true' || v === '1';
  }

  function toNum(v) {
    var n = Number(v);
    return isFinite(n) ? n : 0;
  }

  // 보스 초기화 주기 문자열 → 초기화 종류. "매주 목요일" 같은 표현의 "요일"은 무시한다.
  function cycleToReset(cycle) {
    var s = String(cycle || '').toLowerCase().replace(/[월화수목금토일]요일/g, '');
    if (/주|week/.test(s)) return RESET.WEEKLY_THU;
    if (/월|month/.test(s)) return RESET.MONTHLY;
    if (/일|day|daily/.test(s)) return RESET.DAILY;
    return RESET.WEEKLY_THU;
  }

  function bossGroup(reset) {
    if (reset === RESET.MONTHLY) return 'bossMonthly';
    if (reset === RESET.DAILY) return 'bossDaily';
    return 'bossWeekly';
  }

  function normalizeContent(item, reset, group) {
    var now = toNum(item.now_count);
    var max = toNum(item.max_count);
    var questState = item.quest_state == null ? null : String(item.quest_state);
    var isQuest = item.type === 'quest' || (max <= 0 && questState !== null);
    var done = isQuest ? questState === '2' : (max > 0 && now >= max);
    var name = String(item.content_name || '');
    return {
      key: group + ':' + name,
      group: group,
      name: name,
      type: item.type || 'contents',
      registered: isTrue(item.registration_flag),
      now: now,
      max: max,
      quest: isQuest,
      done: done,
      progress: !done && (questState === '1' || now > 0),
      reset: reset
    };
  }

  // 보스는 난이도가 주마다 바뀔 수 있어서 이름으로만 구분한다
  function normalizeBoss(item) {
    var name = String(item.content_name || '');
    var reset = cycleToReset(item.cycle);
    return {
      key: 'boss:' + name,
      group: bossGroup(reset),
      name: name,
      difficulty: item.difficulty ? String(item.difficulty) : '',
      cycle: item.cycle ? String(item.cycle) : '',
      order: toNum(item.list_order_no),
      registered: isTrue(item.registration_flag),
      done: isTrue(item.complete_flag),
      progress: false,
      reset: reset
    };
  }

  // 데이터가 마지막 초기화(+반영 지연) 이전 것이면 완료 상태를 해제
  function isStale(reset, fetchedAt, now, delay) {
    return fetchedAt < time.lastReset(reset, now) + delay;
  }

  function applyResets(items, fetchedAt, now, delay) {
    items.forEach(function (it) {
      if (isStale(it.reset, fetchedAt, now, delay)) {
        it.stale = true;
        if (it.done || it.progress) {
          it.done = false;
          it.progress = false;
          it.now = 0;
        }
      }
    });
  }

  function count(items) {
    var done = 0;
    items.forEach(function (it) { if (it.done) done++; });
    return { done: done, total: items.length };
  }

  function isCustomized(sel) {
    return !!sel && (!!sel.kept || Object.keys(sel.items || {}).length > 0 || Object.keys(sel.groups || {}).length > 0);
  }

  /**
   * @param body       scheduler/character-state 응답 JSON
   * @param fetchedAt  응답을 받은 시각 (epoch ms)
   * @param now        현재 시각 (epoch ms)
   * @param opts       { showAll: 등록 안 된 항목도 표시, delay: 초기화 후 반영 지연(ms),
   *                     selection: { groups: { 분류: false(숨김) }, items: { 항목 key: true/false },
   *                                  kept: true면 인게임 등록 여부를 보지 않고 items에서 켠 항목만 (유지하기) } }
   */
  function normalize(body, fetchedAt, now, opts) {
    body = body || {};
    opts = opts || {};
    var delay = opts.delay == null ? DATA_DELAY_MS : opts.delay;
    var sel = opts.selection || {};
    var selGroups = sel.groups || {};
    var selItems = sel.items || {};

    var daily = (body.daily_contents || []).map(function (it) { return normalizeContent(it, RESET.DAILY, 'daily'); });
    var weekly = (body.weekly_contents || []).map(function (it) { return normalizeContent(it, RESET.WEEKLY_MON, 'weekly'); });
    var boss = (body.boss_contents || []).map(normalizeBoss);
    boss.sort(function (a, b) { return a.order - b.order; });

    var all = daily.concat(weekly, boss);
    applyResets(all, fetchedAt, now, delay);

    var registered = all.filter(function (it) { return it.registered; }).length;
    var kept = !!sel.kept;
    // 기본 규칙: 등록된 항목만. 등록된 항목이 하나도 없으면 전체를 보여준다
    // 유지 중이면 기본 규칙 대신 "고르지 않은 항목은 숨김"
    var showAll = !kept && (!!opts.showAll || registered === 0);
    // 사용자 선택: 분류를 끄면 그 분류 전체를 숨기고, 항목별 선택은 기본 규칙보다 우선한다
    var hiddenNew = 0;
    all.forEach(function (it) {
      if (typeof selItems[it.key] === 'boolean') it.itemOn = selItems[it.key];
      else {
        it.itemOn = !kept && (showAll || it.registered);
        if (kept && it.registered) hiddenNew++;
      }
      it.visible = it.itemOn && selGroups[it.group] !== false;
    });
    function visible(list) {
      return list.filter(function (it) { return it.visible; });
    }

    var bossClear = toNum(body.weekly_boss_clear_count);
    if (isStale(RESET.WEEKLY_THU, fetchedAt, now, delay)) bossClear = 0;

    var model = {
      name: body.character_name || '',
      world: body.world_name || '',
      level: toNum(body.character_level),
      cls: body.character_class || '',
      date: body.date || null,
      registeredCount: registered,
      showingAll: showAll,
      customized: isCustomized(sel),
      kept: kept,
      hiddenNew: hiddenNew,
      // 표시 설정 화면용: 분류별 전체 항목 (it.visible로 표시 여부)
      groups: GROUPS.map(function (g) {
        return {
          id: g.id,
          label: g.label,
          on: selGroups[g.id] !== false,
          items: all.filter(function (it) { return it.group === g.id; })
        };
      }).filter(function (g) { return g.items.length > 0; }),
      daily: visible(daily),
      weekly: visible(weekly),
      boss: visible(boss),
      bossClear: bossClear,
      bossLimit: toNum(body.weekly_boss_clear_limit_count)
    };
    model.count = {
      daily: count(model.daily),
      weekly: count(model.weekly),
      boss: count(model.boss)
    };
    model.count.all = {
      done: model.count.daily.done + model.count.weekly.done + model.count.boss.done,
      total: model.count.daily.total + model.count.weekly.total + model.count.boss.total
    };
    return model;
  }

  /*
   * 표시 설정 편집. 원본은 그대로 두고 새 선택 객체를 돌려준다.
   * model은 지금 선택으로 normalize한 결과 (항목의 현재 표시 여부를 알기 위해)
   *   item:  항목 하나 켜기/끄기          group: 분류 전체 켜기/끄기
   *   all:   분류의 항목 모두 켜기        none:  분류의 항목 모두 끄기
   */
  function editSelection(sel, model, action, id) {
    var next = {
      groups: Object.assign({}, sel && sel.groups),
      items: Object.assign({}, sel && sel.items)
    };
    if (sel && sel.kept) next.kept = true;
    var groups = (model && model.groups) || [];
    function groupOf(gid) {
      return groups.filter(function (g) { return g.id === gid; })[0];
    }

    if (action === 'item') {
      groups.forEach(function (g) {
        g.items.forEach(function (it) {
          if (it.key === id) next.items[id] = !it.visible;
        });
      });
    } else if (action === 'group') {
      if (next.groups[id] === false) delete next.groups[id];
      else next.groups[id] = false;
    } else if (action === 'keep') {
      // 지금 보이는 대로 모든 항목을 적어 두고 유지 (이후 인게임 등록이 바뀌어도 그대로)
      groups.forEach(function (g) {
        g.items.forEach(function (it) { next.items[it.key] = it.itemOn; });
      });
      next.kept = true;
    } else if (action === 'all' || action === 'none') {
      var g = groupOf(id);
      if (g) g.items.forEach(function (it) { next.items[it.key] = action === 'all'; });
      if (action === 'all') delete next.groups[id];
    }
    return next;
  }

  function hasRegistered(body) {
    var lists = [body && body.daily_contents, body && body.weekly_contents, body && body.boss_contents];
    return lists.some(function (list) {
      return (list || []).some(function (it) { return isTrue(it.registration_flag); });
    });
  }

  var api = {
    normalize: normalize,
    hasRegistered: hasRegistered,
    editSelection: editSelection,
    GROUPS: GROUPS,
    cycleToReset: cycleToReset,
    DATA_DELAY_MS: DATA_DELAY_MS
  };

  root.MH = root.MH || {};
  root.MH.scheduler = api;
  if (typeof module === 'object' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
