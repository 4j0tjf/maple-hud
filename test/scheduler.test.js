const test = require('node:test');
const assert = require('node:assert/strict');
const time = require('../wallpaper/js/time.js');
const scheduler = require('../wallpaper/js/scheduler.js');

const R = time.RESET;
const MIN = 60 * 1000;
// 2026-10-08(목) 21:34 KST
const NOW = Date.parse('2026-10-08T12:34:00Z');

function content(name, extra) {
  return Object.assign({
    content_name: name, type: 'contents', registration_flag: 'true', now_count: 0, max_count: 1, quest_state: null
  }, extra);
}

function boss(name, extra) {
  return Object.assign({
    content_name: name, difficulty: '하드', cycle: '주간', list_order_no: 1, registration_flag: 'true', complete_flag: 'false'
  }, extra);
}

function body(extra) {
  return Object.assign({
    character_name: '테스트', world_name: '스카니아', character_level: 280, character_class: '비숍',
    daily_contents: [], weekly_contents: [], boss_contents: [],
    weekly_boss_clear_count: 0, weekly_boss_clear_limit_count: 12
  }, extra);
}

test('횟수형 콘텐츠와 퀘스트형 콘텐츠의 완료 판정', () => {
  const m = scheduler.normalize(body({
    daily_contents: [
      content('몬스터파크', { now_count: 2, max_count: 2 }),
      content('우르스', { now_count: 1, max_count: 3 }),
      content('세르니움 일일 퀘스트', { type: 'quest', max_count: 0, quest_state: '2' }),
      content('오디움 일일 퀘스트', { type: 'quest', max_count: 0, quest_state: '1' }),
      content('도원경 일일 퀘스트', { type: 'quest', max_count: 0, quest_state: '0' })
    ]
  }), NOW - MIN, NOW);

  assert.deepEqual(m.daily.map((it) => [it.name, it.done, it.progress]), [
    ['몬스터파크', true, false],
    ['우르스', false, true],
    ['세르니움 일일 퀘스트', true, false],
    ['오디움 일일 퀘스트', false, true],
    ['도원경 일일 퀘스트', false, false]
  ]);
  assert.deepEqual(m.count.daily, { done: 2, total: 5 });
});

test('등록된 항목만 보여주고, 등록 항목이 없으면 전체를 보여준다', () => {
  const b = body({
    daily_contents: [content('A'), content('B', { registration_flag: 'false' })],
    boss_contents: [boss('스우', { registration_flag: 'false' })]
  });
  const m = scheduler.normalize(b, NOW - MIN, NOW);
  assert.deepEqual(m.daily.map((it) => it.name), ['A']);
  assert.equal(m.boss.length, 0);
  assert.equal(m.showingAll, false);

  const all = scheduler.normalize(b, NOW - MIN, NOW, { showAll: true });
  assert.equal(all.daily.length, 2);
  assert.equal(all.boss.length, 1);

  const none = scheduler.normalize(body({
    daily_contents: [content('A', { registration_flag: 'false' })]
  }), NOW - MIN, NOW);
  assert.equal(none.showingAll, true);
  assert.equal(none.daily.length, 1);
  assert.equal(scheduler.hasRegistered(b), true);
  assert.equal(scheduler.hasRegistered(body({ daily_contents: [content('A', { registration_flag: false })] })), false);
});

test('보스는 리스트 순서대로 정렬하고 complete_flag로 완료 판정', () => {
  const m = scheduler.normalize(body({
    boss_contents: [
      boss('루시드', { list_order_no: 3, complete_flag: 'true' }),
      boss('스우', { list_order_no: 1 }),
      boss('데미안', { list_order_no: 2, complete_flag: 'true' })
    ],
    weekly_boss_clear_count: 2
  }), NOW - MIN, NOW);
  assert.deepEqual(m.boss.map((it) => [it.name, it.done]), [['스우', false], ['데미안', true], ['루시드', true]]);
  assert.equal(m.bossClear, 2);
  assert.equal(m.bossLimit, 12);
});

test('보스 초기화 주기 해석', () => {
  assert.equal(scheduler.cycleToReset('주간'), R.WEEKLY_THU);
  assert.equal(scheduler.cycleToReset('매주 목요일'), R.WEEKLY_THU);
  assert.equal(scheduler.cycleToReset('월간'), R.MONTHLY);
  assert.equal(scheduler.cycleToReset('일간'), R.DAILY);
  assert.equal(scheduler.cycleToReset('daily'), R.DAILY);
  assert.equal(scheduler.cycleToReset('monthly'), R.MONTHLY);
  assert.equal(scheduler.cycleToReset(null), R.WEEKLY_THU);
});

test('자정이 지나면 이전 데이터의 일일 완료 표시를 해제한다', () => {
  const b = body({
    daily_contents: [content('몬스터파크', { now_count: 2, max_count: 2 })],
    weekly_contents: [content('무릉도장', { now_count: 1, max_count: 1 })]
  });
  const fetched = Date.parse('2026-10-09T14:50:00Z'); // 금 23:50 KST
  const later = Date.parse('2026-10-09T16:00:00Z');   // 토 01:00 KST
  const m = scheduler.normalize(b, fetched, later);
  assert.equal(m.daily[0].done, false);
  assert.equal(m.daily[0].now, 0);
  assert.equal(m.weekly[0].done, true, '주간 콘텐츠는 월요일에만 초기화');
});

test('초기화 직후 반영 지연 동안 받은 데이터도 초기화된 것으로 본다', () => {
  const b = body({ daily_contents: [content('몬스터파크', { now_count: 2, max_count: 2 })] });
  const reset = Date.parse('2026-10-08T15:00:00Z');
  assert.equal(scheduler.normalize(b, reset + 5 * MIN, reset + 6 * MIN).daily[0].done, false);
  assert.equal(scheduler.normalize(b, reset + 25 * MIN, reset + 26 * MIN).daily[0].done, true);
  assert.equal(scheduler.normalize(b, reset + 5 * MIN, reset + 6 * MIN, { delay: 0 }).daily[0].done, true);
});

test('목요일 초기화가 지나면 주간 보스와 클리어 횟수를 초기화한다', () => {
  const b = body({
    boss_contents: [boss('스우', { complete_flag: 'true' }), boss('검은 마법사', { cycle: '월간', complete_flag: 'true' })],
    weekly_boss_clear_count: 5
  });
  const fetched = Date.parse('2026-10-07T14:00:00Z'); // 수 23:00 KST
  const m = scheduler.normalize(b, fetched, NOW);
  assert.equal(m.boss[0].done, false);
  assert.equal(m.boss[1].done, true, '월간 보스는 1일에 초기화');
  assert.equal(m.bossClear, 0);
});
