/*
 * KST(UTC+9) 기준 초기화 시각 계산.
 * 메이플스토리 초기화: 일일 00:00, 주간 보스 목요일 00:00, 주간 콘텐츠 월요일 00:00, 월간 1일 00:00.
 */
(function (root) {
  'use strict';

  var KST_OFFSET = 9 * 3600 * 1000;
  var DAY = 86400000;
  var WEEKDAYS = ['일', '월', '화', '수', '목', '금', '토'];

  var RESET = {
    DAILY: 'daily',
    WEEKLY_MON: 'weekly-mon',
    WEEKLY_THU: 'weekly-thu',
    MONTHLY: 'monthly'
  };

  function mod(a, b) {
    return ((a % b) + b) % b;
  }

  // KST 시각을 UTC getter로 읽을 수 있게 옮긴 Date
  function kstDate(now) {
    return new Date(now + KST_OFFSET);
  }

  function kstMidnight(now) {
    var k = now + KST_OFFSET;
    return k - mod(k, DAY) - KST_OFFSET;
  }

  // dow: 0=일 ... 6=토 (KST 기준 요일)
  function lastWeekly(now, dow) {
    var diff = mod(kstDate(now).getUTCDay() - dow, 7);
    return kstMidnight(now) - diff * DAY;
  }

  function lastMonthly(now) {
    var d = kstDate(now);
    return Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), 1) - KST_OFFSET;
  }

  function nextMonthly(now) {
    var d = kstDate(now);
    return Date.UTC(d.getUTCFullYear(), d.getUTCMonth() + 1, 1) - KST_OFFSET;
  }

  function lastReset(kind, now) {
    switch (kind) {
      case RESET.WEEKLY_MON: return lastWeekly(now, 1);
      case RESET.WEEKLY_THU: return lastWeekly(now, 4);
      case RESET.MONTHLY: return lastMonthly(now);
      default: return kstMidnight(now);
    }
  }

  function nextReset(kind, now) {
    switch (kind) {
      case RESET.WEEKLY_MON: return lastWeekly(now, 1) + 7 * DAY;
      case RESET.WEEKLY_THU: return lastWeekly(now, 4) + 7 * DAY;
      case RESET.MONTHLY: return nextMonthly(now);
      default: return kstMidnight(now) + DAY;
    }
  }

  function pad(n) {
    return (n < 10 ? '0' : '') + n;
  }

  // 1일 이상이면 "2일 03:12", 미만이면 "03:12:45"
  function formatCountdown(ms) {
    var s = Math.max(0, Math.floor(ms / 1000));
    var d = Math.floor(s / 86400);
    var h = Math.floor((s % 86400) / 3600);
    var m = Math.floor((s % 3600) / 60);
    if (d > 0) return d + '일 ' + pad(h) + ':' + pad(m);
    return pad(h) + ':' + pad(m) + ':' + pad(s % 60);
  }

  function formatClock(now) {
    var d = kstDate(now);
    return {
      date: pad(d.getUTCMonth() + 1) + '.' + pad(d.getUTCDate()) + ' (' + WEEKDAYS[d.getUTCDay()] + ')',
      time: pad(d.getUTCHours()) + ':' + pad(d.getUTCMinutes()),
      seconds: pad(d.getUTCSeconds())
    };
  }

  function formatAgo(ms) {
    var m = Math.floor(ms / 60000);
    if (m < 1) return '방금';
    if (m < 60) return m + '분 전';
    var h = Math.floor(m / 60);
    if (h < 24) return h + '시간 전';
    return Math.floor(h / 24) + '일 전';
  }

  var api = {
    RESET: RESET,
    DAY: DAY,
    lastReset: lastReset,
    nextReset: nextReset,
    formatCountdown: formatCountdown,
    formatClock: formatClock,
    formatAgo: formatAgo
  };

  root.MH = root.MH || {};
  root.MH.time = api;
  if (typeof module === 'object' && module.exports) module.exports = api;
})(typeof window !== 'undefined' ? window : globalThis);
