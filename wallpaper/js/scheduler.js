/*
 * /maplestory/v1/scheduler/character-state 응답을 HUD용 모델로 정리한다.
 * - 인게임 스케줄러에 등록된(registration_flag) 항목만 보여주는 것이 기본
 * - 데이터를 받은 뒤 초기화 시각이 지났다면 완료 표시를 미리 해제한다 (API 반영 지연 대비)
 */
(function (root) {
  'use strict';

  var time = root.MH && root.MH.time;
  if (!time && typeof require === 'function') time = require('./time.js');
  var RESET = time.RESET;
  // 초기화 직후 API는 한동안(평균 15분) 이전 데이터를 줄 수 있다
  var DATA_DELAY_MS = 20 * 60 * 1000;

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

  function normalizeContent(item, reset) {
    var now = toNum(item.now_count);
    var max = toNum(item.max_count);
    var questState = item.quest_state == null ? null : String(item.quest_state);
    var isQuest = item.type === 'quest' || (max <= 0 && questState !== null);
    var done = isQuest ? questState === '2' : (max > 0 && now >= max);
    return {
      name: String(item.content_name || ''),
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

  function normalizeBoss(item) {
    return {
      name: String(item.content_name || ''),
      difficulty: item.difficulty ? String(item.difficulty) : '',
      cycle: item.cycle ? String(item.cycle) : '',
      order: toNum(item.list_order_no),
      registered: isTrue(item.registration_flag),
      done: isTrue(item.complete_flag),
      progress: false,
      reset: cycleToReset(item.cycle)
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

  /**
   * @param body       scheduler/character-state 응답 JSON
   * @param fetchedAt  응답을 받은 시각 (epoch ms)
   * @param now        현재 시각 (epoch ms)
   * @param opts       { showAll: 등록 안 된 항목도 표시, delay: 초기화 후 반영 지연(ms) }
   */
  function normalize(body, fetchedAt, now, opts) {
    body = body || {};
    opts = opts || {};
    var delay = opts.delay == null ? DATA_DELAY_MS : opts.delay;

    var daily = (body.daily_contents || []).map(function (it) { return normalizeContent(it, RESET.DAILY); });
    var weekly = (body.weekly_contents || []).map(function (it) { return normalizeContent(it, RESET.WEEKLY_MON); });
    var boss = (body.boss_contents || []).map(normalizeBoss);
    boss.sort(function (a, b) { return a.order - b.order; });

    var all = daily.concat(weekly, boss);
    applyResets(all, fetchedAt, now, delay);

    var registered = all.filter(function (it) { return it.registered; }).length;
    // 등록된 항목이 하나도 없으면 전체를 보여준다
    var showAll = !!opts.showAll || registered === 0;
    function visible(list) {
      return showAll ? list : list.filter(function (it) { return it.registered; });
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

  function hasRegistered(body) {
    var lists = [body && body.daily_contents, body && body.weekly_contents, body && body.boss_contents];
    return lists.some(function (list) {
      return (list || []).some(function (it) { return isTrue(it.registration_flag); });
    });
  }

  var api = {
    normalize: normalize,
    hasRegistered: hasRegistered,
    cycleToReset: cycleToReset,
    DATA_DELAY_MS: DATA_DELAY_MS
  };

  root.MH = root.MH || {};
  root.MH.scheduler = api;
  if (typeof module === 'object' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
