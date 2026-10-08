/* localStorage 래퍼. 저장소를 쓸 수 없으면 메모리에만 보관한다. */
(function (root) {
  'use strict';

  var PREFIX = 'mhud:';
  var memory = {};
  var ls = null;
  try {
    ls = root.localStorage;
    ls.setItem(PREFIX + 'probe', '1');
    ls.removeItem(PREFIX + 'probe');
  } catch (e) {
    ls = null;
  }

  function get(key) {
    var raw = null;
    try {
      raw = ls ? ls.getItem(PREFIX + key) : memory[key];
    } catch (e) {
      raw = memory[key];
    }
    if (raw == null) return null;
    try {
      return JSON.parse(raw);
    } catch (e) {
      return null;
    }
  }

  function set(key, value) {
    var raw = JSON.stringify(value);
    memory[key] = raw;
    try {
      if (ls) ls.setItem(PREFIX + key, raw);
    } catch (e) { /* 용량 초과 등은 무시 */ }
  }

  function remove(key) {
    delete memory[key];
    try {
      if (ls) ls.removeItem(PREFIX + key);
    } catch (e) { /* ignore */ }
  }

  // { at, data } 형태로 저장된 캐시. maxAge 안이면 반환
  function getCached(key, maxAge, now) {
    var entry = get(key);
    if (!entry || typeof entry.at !== 'number') return null;
    if (maxAge != null && (now || Date.now()) - entry.at > maxAge) return null;
    return entry;
  }

  function setCached(key, data, now) {
    var entry = { at: now || Date.now(), data: data };
    set(key, entry);
    return entry;
  }

  root.MH = root.MH || {};
  root.MH.store = {
    get: get,
    set: set,
    remove: remove,
    getCached: getCached,
    setCached: setCached
  };
})(typeof window !== 'undefined' ? window : globalThis);
