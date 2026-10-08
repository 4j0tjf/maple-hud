const test = require('node:test');
const assert = require('node:assert/strict');
const time = require('../wallpaper/js/time.js');

const R = time.RESET;
// 2026-10-08(목) 21:34 KST
const NOW = Date.parse('2026-10-08T12:34:00Z');

test('일일 초기화는 KST 자정', () => {
  assert.equal(time.lastReset(R.DAILY, NOW), Date.parse('2026-10-07T15:00:00Z'));
  assert.equal(time.nextReset(R.DAILY, NOW), Date.parse('2026-10-08T15:00:00Z'));
});

test('주간 보스는 목요일, 주간 콘텐츠는 월요일 KST 자정', () => {
  assert.equal(time.lastReset(R.WEEKLY_THU, NOW), Date.parse('2026-10-07T15:00:00Z'));
  assert.equal(time.nextReset(R.WEEKLY_THU, NOW), Date.parse('2026-10-14T15:00:00Z'));
  assert.equal(time.lastReset(R.WEEKLY_MON, NOW), Date.parse('2026-10-04T15:00:00Z'));
  assert.equal(time.nextReset(R.WEEKLY_MON, NOW), Date.parse('2026-10-11T15:00:00Z'));
});

test('목요일 자정 직전에는 지난주 목요일이 기준', () => {
  const justBefore = Date.parse('2026-10-07T14:59:59.999Z'); // 수 23:59:59 KST
  assert.equal(time.lastReset(R.WEEKLY_THU, justBefore), Date.parse('2026-09-30T15:00:00Z'));
  const exactly = Date.parse('2026-10-07T15:00:00Z');
  assert.equal(time.lastReset(R.WEEKLY_THU, exactly), exactly);
});

test('UTC로는 전날이어도 KST 날짜 기준으로 계산', () => {
  // 2026-10-08 01:00 KST = 2026-10-07 16:00 UTC (UTC 기준 수요일)
  const t = Date.parse('2026-10-07T16:00:00Z');
  assert.equal(time.lastReset(R.DAILY, t), Date.parse('2026-10-07T15:00:00Z'));
  assert.equal(time.lastReset(R.WEEKLY_THU, t), Date.parse('2026-10-07T15:00:00Z'));
});

test('월간 초기화는 매월 1일 KST 자정 (연말 포함)', () => {
  assert.equal(time.lastReset(R.MONTHLY, NOW), Date.parse('2026-09-30T15:00:00Z'));
  assert.equal(time.nextReset(R.MONTHLY, NOW), Date.parse('2026-10-31T15:00:00Z'));
  const dec = Date.parse('2026-12-20T00:00:00Z');
  assert.equal(time.nextReset(R.MONTHLY, dec), Date.parse('2026-12-31T15:00:00Z'));
});

test('카운트다운 표시', () => {
  assert.equal(time.formatCountdown(((2 * 24 + 3) * 3600 + 12 * 60 + 59) * 1000), '2일 03:12');
  assert.equal(time.formatCountdown((3 * 3600 + 12 * 60 + 45) * 1000), '03:12:45');
  assert.equal(time.formatCountdown(-5), '00:00:00');
});

test('초 없이 분 단위 카운트다운 (남은 분은 올림)', () => {
  assert.equal(time.formatCountdown((3 * 3600 + 12 * 60 + 45) * 1000, false), '3시간 13분');
  assert.equal(time.formatCountdown((59 * 60 + 1) * 1000, false), '1시간 00분');
  assert.equal(time.formatCountdown(30 * 1000, false), '1분');
  assert.equal(time.formatCountdown(((2 * 24 + 3) * 3600) * 1000, false), '2일 03:00');
});

test('KST 시계 표시', () => {
  assert.deepEqual(time.formatClock(NOW), { date: '10.08 (목)', time: '21:34', seconds: '00' });
});
