/* HUD 렌더링: 배경, 패널 스타일, 캐릭터 카드 */
(function (root) {
  'use strict';

  var MH = root.MH;
  var time = MH.time;
  var doc = root.document;

  function $(id) {
    return doc.getElementById(id);
  }

  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  var ICON = {
    check: '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M3.6 8.4l2.9 2.9 5.9-6.6" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>',
    warn: '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M8 1.8l6.6 11.7H1.4z" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/><path d="M8 6.2v3.4M8 11.6v.1" stroke="currentColor" stroke-width="1.6" stroke-linecap="round"/></svg>',
    edit: '<svg viewBox="0 0 16 16" aria-hidden="true"><path d="M10.6 2.6l2.8 2.8-7.6 7.6-3.4.6.6-3.4z" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linejoin="round"/><path d="M9.2 4l2.8 2.8" stroke="currentColor" stroke-width="1.5"/></svg>'
  };

  var DIFFICULTY = {
    '이지': 'easy', '노멀': 'normal', '하드': 'hard', '카오스': 'chaos', '익스트림': 'extreme'
  };

  var ALIGN = { left: 'flex-start', top: 'flex-start', center: 'center', right: 'flex-end', bottom: 'flex-end' };

  /* ---------- 스타일 / 배경 ---------- */

  function hexToRgb(hex) {
    var m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex || '');
    if (!m) return '255, 181, 71';
    return [m[1], m[2], m[3]].map(function (h) { return parseInt(h, 16); }).join(', ');
  }

  var currentScale = 1;

  function applyStyle(cfg) {
    var rs = doc.documentElement.style;
    var scale = Math.max(0.4, Math.min(3, (cfg.scale || 100) / 100));
    currentScale = scale;
    rs.setProperty('--accent', cfg.accent);
    rs.setProperty('--accent-rgb', hexToRgb(cfg.accent));
    rs.setProperty('--panel-alpha', String(Math.max(0, Math.min(100, cfg.opacity)) / 100));
    rs.setProperty('--panel-blur', Math.max(0, cfg.blur) + 'px');
    rs.setProperty('--cols', String(Math.max(1, Math.round(cfg.columns) || 1)));
    rs.setProperty('--card-w', Math.max(240, cfg.cardWidth) + 'px');
    rs.setProperty('--zoom', String(scale));

    var stage = $('stage');
    stage.style.justifyContent = ALIGN[cfg.alignX] || 'flex-end';
    stage.style.alignItems = ALIGN[cfg.alignY] || 'flex-start';
    stage.style.padding = Math.max(0, cfg.offsetY) + 'px ' + Math.max(0, cfg.offsetX) + 'px';
    fitHeight(cfg);
    applyBackground(cfg);
  }

  function fitHeight(cfg) {
    var avail = root.innerHeight - 2 * Math.max(0, cfg.offsetY);
    $('hud').style.maxHeight = Math.max(160, avail / currentScale) + 'px';
    updateOverflow();
  }

  var currentVideo = '';

  function applyBackground(cfg) {
    var bg = $('bg');
    var img = $('bg-image');
    var vid = $('bg-video');
    var type = cfg.bgType;
    var src = '';
    if (type === 'image') src = cfg.bgImage || cfg.bgUrl;
    if (type === 'video') src = cfg.bgVideo || cfg.bgUrl;

    bg.style.backgroundColor = cfg.bgColor;
    bg.setAttribute('data-type', src || type === 'color' ? type : 'default');
    doc.documentElement.style.setProperty('--bg-fit', cfg.bgFit || 'cover');
    doc.documentElement.style.setProperty('--bg-dim', String(Math.max(0, Math.min(90, cfg.bgDim)) / 100));

    if (type === 'image' && src) {
      if (img.getAttribute('src') !== src) img.setAttribute('src', src);
    } else {
      img.removeAttribute('src');
    }

    if (type === 'video' && src) {
      if (currentVideo !== src) {
        currentVideo = src;
        vid.src = src;
      }
      var p = vid.play();
      if (p && p.catch) p.catch(function () {});
    } else if (currentVideo) {
      currentVideo = '';
      vid.pause();
      vid.removeAttribute('src');
      vid.load();
    }
  }

  function setPaused(paused) {
    var vid = $('bg-video');
    doc.documentElement.classList.toggle('paused', paused);
    if (!currentVideo) return;
    if (paused) {
      vid.pause();
    } else {
      var p = vid.play();
      if (p && p.catch) p.catch(function () {});
    }
  }

  /* ---------- 카드 ---------- */

  // 일일/주간 섹션 안에서는 "OO 일일 퀘스트"의 꼬리말이 중복이라 줄인다
  function shortName(name) {
    var s = String(name).replace(/\s*(일일|주간)\s*퀘스트\s*$/, '');
    return s || name;
  }

  function chip(item, isBoss) {
    var state = item.done ? 'done' : (item.progress ? 'prog' : 'todo');
    var label = isBoss ? item.name : shortName(item.name);
    var extra = '';
    if (isBoss && item.difficulty) {
      extra = '<span class="diff diff-' + (DIFFICULTY[item.difficulty] || 'etc') + '">' + esc(item.difficulty) + '</span>';
    } else if (!item.quest && item.max > 1) {
      extra = '<span class="ct">' + item.now + '/' + item.max + '</span>';
    }
    var title = item.name + (isBoss && item.difficulty ? ' (' + item.difficulty + ')' : '');
    if (isBoss && item.reset === time.RESET.MONTHLY) extra += '<span class="cyc">월</span>';
    if (isBoss && item.reset === time.RESET.DAILY) extra += '<span class="cyc">일</span>';
    return '<li class="chip ' + state + (item.registered ? '' : ' unreg') + '" title="' + esc(title) + '">' +
      '<i class="dot">' + (item.done ? ICON.check : '') + '</i>' +
      '<span class="nm">' + esc(label) + '</span>' + extra + '</li>';
  }

  function section(title, items, counter, opts) {
    if (!items.length) return '';
    var shown = opts.hideDone ? items.filter(function (it) { return !it.done; }) : items;
    var body = shown.length
      ? '<ul class="chips">' + shown.map(function (it) { return chip(it, opts.boss); }).join('') + '</ul>'
      : '<div class="sec-clear">' + ICON.check + '모두 완료</div>';
    var allDone = counter.total > 0 && counter.done === counter.total;
    return '<div class="sec' + (opts.boss ? ' sec-boss' : '') + (allDone ? ' sec-done' : '') + '">' +
      '<div class="sec-head"><span class="sec-title">' + title + '</span>' +
      '<span class="sec-count">' + counter.done + '<span>/' + counter.total + '</span></span>' +
      (opts.note ? '<span class="sec-note">' + opts.note + '</span>' : '') +
      '</div>' + body + '</div>';
  }

  function avatar(card) {
    if (card.image) {
      return '<div class="avatar"><img src="' + esc(card.image) + '" alt="" referrerpolicy="no-referrer" ' +
        'onerror="this.parentNode.classList.add(\'noimg\');this.remove()"><b>' + esc(card.name.charAt(0)) + '</b></div>';
    }
    return '<div class="avatar noimg"><b>' + esc(card.name.charAt(0)) + '</b></div>';
  }

  function tally(label, c) {
    if (!c.total) return '';
    var cls = c.done === c.total ? ' ok' : '';
    return '<span class="t' + cls + '"><em>' + label + '</em>' + c.done + '/' + c.total + '</span>';
  }

  /* ---------- 표시 항목 편집 ---------- */

  function editChip(card, group, it) {
    var label = group.id === 'daily' || group.id === 'weekly' ? shortName(it.name) : it.name;
    var diff = it.difficulty
      ? '<span class="diff diff-' + (DIFFICULTY[it.difficulty] || 'etc') + '">' + esc(it.difficulty) + '</span>'
      : '';
    var title = it.name + (it.difficulty ? ' (' + it.difficulty + ')' : '') + (it.registered ? ' · 인게임 스케줄러에 등록됨' : '');
    return '<li class="chip pick ' + (it.visible ? 'on' : 'off') + '" data-action="item" data-key="' + esc(card.key) +
      '" data-id="' + esc(it.key) + '" title="' + esc(title) + '">' +
      '<i class="dot">' + (it.visible ? ICON.check : '') + '</i>' +
      '<span class="nm">' + esc(label) + '</span>' + diff +
      (it.registered ? '<span class="reg">★</span>' : '') + '</li>';
  }

  function editorHtml(card) {
    var m = card.model;
    var key = ' data-key="' + esc(card.key) + '"';
    var groups = m.groups.map(function (g) {
      var shown = g.on ? g.items.filter(function (it) { return it.visible; }).length : 0;
      return '<div class="ed-group' + (g.on ? '' : ' off') + '">' +
        '<div class="ed-head">' +
          '<button type="button" class="switch' + (g.on ? ' on' : '') + '" data-action="group"' + key +
            ' data-id="' + g.id + '" title="' + g.label + ' 전체 보이기/숨기기"><i></i></button>' +
          '<span class="ed-title">' + g.label + '</span>' +
          '<span class="ed-count">' + shown + '/' + g.items.length + '</span>' +
          (g.on
            ? '<button type="button" class="ed-link" data-action="all"' + key + ' data-id="' + g.id + '">전체 선택</button>' +
              '<button type="button" class="ed-link" data-action="none"' + key + ' data-id="' + g.id + '">전체 해제</button>'
            : '<span class="ed-off">숨김</span>') +
        '</div>' +
        (g.on ? '<ul class="chips">' + g.items.map(function (it) { return editChip(card, g, it); }).join('') + '</ul>' : '') +
      '</div>';
    }).join('');

    return '<div class="editor">' +
      '<div class="ed-top">' +
        '<span class="ed-help">표시할 항목을 누르세요 <span class="reg">★</span> 인게임 스케줄러 등록</span>' +
        (m.customized ? '<button type="button" class="ed-link" data-action="reset"' + key + '>기본값으로</button>' : '') +
        '<button type="button" class="ed-done" data-action="edit"' + key + '>완료</button>' +
      '</div>' +
      (groups || '<div class="card-msg">스케줄러 항목이 없습니다</div>') +
    '</div>';
  }

  function cardHtml(card, cfg) {
    var m = card.model;
    var complete = m && m.count.all.total > 0 && m.count.all.done === m.count.all.total;
    var pct = m && m.count.all.total ? Math.round(m.count.all.done / m.count.all.total * 100) : 0;
    var cls = ['card'];
    if (card.collapsed && !card.editing) cls.push('collapsed');
    if (card.editing) cls.push('editing');
    if (complete) cls.push('complete');
    if (card.loading) cls.push('loading');
    if (!m) cls.push('empty');

    var meta = [card.cls, card.world].filter(Boolean).map(esc).join(' · ');
    var lv = card.level ? '<span class="lv">Lv.' + card.level + '</span>' : '';
    var exp = card.exp ? '<span class="exp">' + esc(card.exp) + '%</span>' : '';
    var warn = card.error && m ? '<span class="warn" title="' + esc(card.error) + '">' + ICON.warn + '</span>' : '';

    var head =
      '<div class="card-head" data-action="toggle" data-key="' + esc(card.key) + '">' +
        avatar(card) +
        '<div class="who">' +
          '<div class="name-row"><span class="name">' + esc(card.name) + '</span>' + lv + exp + warn + '</div>' +
          '<div class="meta">' + meta + '</div>' +
          (m ? '<div class="bar"><i style="width:' + pct + '%"></i></div>' : '') +
        '</div>' +
        (complete ? '<span class="badge-clear">ALL CLEAR</span>'
          : m ? '<div class="tally">' + tally('일일', m.count.daily) + tally('주간', m.count.weekly) + tally('보스', m.count.boss) + '</div>' : '') +
        (m ? '<button type="button" class="card-edit' + (card.editing ? ' on' : '') + '" data-action="edit" data-key="' + esc(card.key) +
          '" title="표시할 항목 고르기">' + ICON.edit + '</button>' : '') +
      '</div>';

    var body = '';
    if (m && card.editing) {
      body = editorHtml(card);
    } else if (m) {
      var opts = { hideDone: cfg.hideDone };
      var bossNote = m.bossLimit ? '주간 클리어 ' + m.bossClear + '/' + m.bossLimit : '';
      body = section('일일', m.daily, m.count.daily, opts) +
        section('주간', m.weekly, m.count.weekly, opts) +
        section('보스', m.boss, m.count.boss, { hideDone: cfg.hideDone, boss: true, note: bossNote });
      if (!body) body = '<div class="card-msg">표시할 항목이 없습니다. ✎ 버튼으로 고르세요</div>';
      if (m.showingAll && !cfg.showAll && m.registeredCount === 0 && !m.customized) {
        body = '<div class="card-hint">인게임 스케줄러에 등록된 항목이 없어 전체 항목을 표시합니다</div>' + body;
      }
    } else if (card.error) {
      body = '<div class="card-msg error">' + ICON.warn + esc(card.error) + '</div>';
    } else {
      body = '<div class="skeleton"><i></i><i></i><i></i></div>';
    }

    return '<article class="' + cls.join(' ') + '">' + head + '<div class="card-body">' + body + '</div></article>';
  }

  /* ---------- 전체 렌더 ---------- */

  function render(view, cfg) {
    var notices = [];
    if (view.demo) {
      notices.push({ level: 'info', text: view.isWE
        ? '데모 데이터입니다. 배경화면 속성에서 넥슨 Open API 키를 입력하세요.'
        : '데모 데이터입니다. 패널 위쪽 ⚙ 버튼에서 넥슨 Open API 키를 입력하세요.' });
    }
    if (view.notice) notices.push(view.notice);
    $('notice').innerHTML = notices.map(function (n) {
      return '<div class="notice notice-' + n.level + '">' + (n.level === 'info' ? '' : ICON.warn) + '<span>' + esc(n.text) + '</span></div>';
    }).join('');

    var cards = view.cards;
    if (!cards.length && !view.syncing) {
      $('cards').innerHTML = '<div class="empty-state">표시할 캐릭터가 없습니다</div>';
    } else {
      $('cards').innerHTML = cards.map(function (c) { return cardHtml(c, cfg); }).join('');
    }

    var remain = { daily: 0, weekly: 0, boss: 0 };
    cards.forEach(function (c) {
      if (!c.model) return;
      remain.daily += c.model.count.daily.total - c.model.count.daily.done;
      remain.weekly += c.model.count.weekly.total - c.model.count.weekly.done;
      remain.boss += c.model.count.boss.total - c.model.count.boss.done;
    });
    $('summary').innerHTML =
      '<span><em>남은 일일</em>' + remain.daily + '</span>' +
      '<span><em>주간</em>' + remain.weekly + '</span>' +
      '<span><em>보스</em>' + remain.boss + '</span>';

    $('btn-settings').hidden = !!view.isWE;
    updateOverflow();
    tick(view, Date.now());
  }

  function updateOverflow() {
    var el = $('cards');
    el.classList.toggle('more', el.scrollHeight > el.clientHeight + 2);
    el.classList.toggle('at-end', el.scrollTop + el.clientHeight >= el.scrollHeight - 2);
  }

  // 같은 값이면 DOM을 건드리지 않는다 (매초 불필요한 스타일 계산·레이아웃 방지)
  var lastText = {};
  function setText(id, value) {
    if (lastText[id] === value) return;
    lastText[id] = value;
    $(id).textContent = value;
  }

  // 1초마다 갱신되는 부분 (시계, 초기화 카운트다운, 동기화 상태)
  // 초 표시를 끄면 1분에 한 번만 값이 바뀌어서 화면도 그때만 다시 그린다
  function tick(view, now) {
    var sec = view.showSeconds !== false;
    var clock = time.formatClock(now);
    setText('clock-date', clock.date);
    setText('clock-time', clock.time);
    setText('clock-sec', sec ? clock.seconds : '');

    var R = time.RESET;
    setText('reset-daily', time.formatCountdown(time.nextReset(R.DAILY, now) - now, sec));
    setText('reset-boss', time.formatCountdown(time.nextReset(R.WEEKLY_THU, now) - now, sec));
    setText('reset-weekly', time.formatCountdown(time.nextReset(R.WEEKLY_MON, now) - now, sec));

    var sync = $('sync');
    sync.classList.toggle('busy', !!view.syncing);
    sync.classList.toggle('error', !!(view.notice && view.notice.level === 'error'));
    var text;
    if (view.syncing) text = '동기화 중…';
    else if (view.demo) text = '데모 모드';
    else if (view.lastSync) text = time.formatAgo(now - view.lastSync) + ' 동기화';
    else if (view.notice && view.notice.level === 'error') text = '동기화 실패';
    else text = '대기 중';
    setText('sync-text', text);
  }

  function init(handlers) {
    $('cards').addEventListener('click', function (e) {
      var el = e.target.closest('[data-action]');
      if (!el) return;
      var action = el.getAttribute('data-action');
      var key = el.getAttribute('data-key');
      if (action === 'toggle') handlers.onToggle(key);
      else if (action === 'edit') handlers.onEdit(key);
      else handlers.onSelect(key, action, el.getAttribute('data-id'));
    });
    $('cards').addEventListener('scroll', updateOverflow, { passive: true });
    $('btn-refresh').addEventListener('click', function () { handlers.onRefresh(); });
    $('btn-settings').addEventListener('click', function () { handlers.onSettings(); });
  }

  MH.hud = {
    init: init,
    applyStyle: applyStyle,
    fitHeight: fitHeight,
    setPaused: setPaused,
    render: render,
    tick: tick
  };
})(window);
