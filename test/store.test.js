const test = require('node:test');
const assert = require('node:assert/strict');

// store.js가 쓰는 localStorage를 흉내낸다
const data = new Map();
globalThis.localStorage = {
  getItem: (k) => (data.has(k) ? data.get(k) : null),
  setItem: (k, v) => data.set(k, String(v)),
  removeItem: (k) => data.delete(k),
  key: (i) => [...data.keys()][i] ?? null,
  get length() { return data.size; }
};
require('../wallpaper/js/store.js');
const store = globalThis.MH.store;

const DAY = 24 * 3600 * 1000;

test('오래된 캐시만 정리하고 설정·접기 상태는 남긴다', () => {
  const now = Date.parse('2026-10-08T00:00:00Z');
  store.setCached('sched:old', { a: 1 }, now - 40 * DAY);
  store.setCached('sched:new', { a: 2 }, now - 2 * DAY);
  store.set('browserConfig', { apikey: 'x' });
  store.set('collapse:o1', { v: true, day: now - 90 * DAY });
  data.set('other-app-key', JSON.stringify({ at: 0 }));

  assert.equal(store.prune(31 * DAY, now), 1);
  assert.equal(store.get('sched:old'), null);
  assert.deepEqual(store.getCached('sched:new', null, now).data, { a: 2 });
  assert.deepEqual(store.get('browserConfig'), { apikey: 'x' });
  assert.ok(store.get('collapse:o1'));
  assert.ok(data.has('other-app-key'), '다른 키는 건드리지 않는다');
});

test('캐시 유효 시간', () => {
  const now = Date.parse('2026-10-08T00:00:00Z');
  store.setCached('ttl', 1, now - 10 * 60 * 1000);
  assert.ok(store.getCached('ttl', 15 * 60 * 1000, now));
  assert.equal(store.getCached('ttl', 5 * 60 * 1000, now), null);
});
