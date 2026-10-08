/*
 * 넥슨 Open API (메이플스토리) 클라이언트.
 * 요청은 순차 큐로 보내고 간격을 둬서 초당 호출 제한(개발 키 기준 5회)을 넘지 않게 한다.
 */
(function (root) {
  'use strict';

  var DEFAULT_BASE = 'https://open.api.nexon.com';
  var MIN_GAP_MS = 260;
  var TIMEOUT_MS = 12000;

  var MESSAGES = {
    OPENAPI00001: '넥슨 API 서버 내부 오류',
    OPENAPI00002: 'API 키에 권한이 없습니다',
    OPENAPI00003: '유효하지 않은 캐릭터 식별자입니다',
    OPENAPI00004: '요청 값이 올바르지 않습니다 (캐릭터명 확인)',
    OPENAPI00005: 'API 키가 올바르지 않습니다',
    OPENAPI00006: '잘못된 API 경로입니다',
    OPENAPI00007: 'API 호출 한도를 초과했습니다',
    OPENAPI00009: '데이터 준비 중입니다',
    OPENAPI00010: '게임 점검 중입니다',
    OPENAPI00011: 'API 점검 중입니다',
    NETWORK: '네트워크 오류 (인터넷 연결 또는 CORS 차단 — README의 프록시 안내 참고)',
    TIMEOUT: '응답 시간 초과'
  };

  function ApiError(code, message, status) {
    this.name = 'ApiError';
    this.code = code;
    this.status = status || 0;
    this.message = MESSAGES[code] || message || code;
    this.detail = message || '';
  }
  ApiError.prototype = Object.create(Error.prototype);
  ApiError.prototype.constructor = ApiError;

  var queue = Promise.resolve();
  var lastAt = 0;

  function sleep(ms) {
    return new Promise(function (resolve) { setTimeout(resolve, ms); });
  }

  function enqueue(task) {
    var run = function () {
      var wait = Math.max(0, lastAt + MIN_GAP_MS - Date.now());
      return sleep(wait).then(function () {
        lastAt = Date.now();
        return task();
      });
    };
    var p = queue.then(run, run);
    queue = p.catch(function () {});
    return p;
  }

  function buildUrl(base, path, params) {
    var url = String(base || DEFAULT_BASE).replace(/\/+$/, '') + '/maplestory/v1/' + path;
    var qs = Object.keys(params || {})
      .filter(function (k) { return params[k] != null && params[k] !== ''; })
      .map(function (k) { return encodeURIComponent(k) + '=' + encodeURIComponent(params[k]); })
      .join('&');
    return qs ? url + '?' + qs : url;
  }

  // 오버레이 앱에서는 메인 프로세스가 대신 요청한다 (브라우저 CORS 제한을 받지 않음)
  function httpGet(url, headers, signal) {
    var overlay = root.mapleOverlay;
    if (overlay && overlay.httpGet) {
      return overlay.httpGet(url, headers).then(function (r) {
        return {
          ok: r.status >= 200 && r.status < 300,
          status: r.status,
          text: function () { return Promise.resolve(r.body); }
        };
      });
    }
    return fetch(url, { headers: headers, signal: signal, cache: 'no-store' });
  }

  function fetchOnce(opts, path, params) {
    var controller = typeof AbortController === 'function' ? new AbortController() : null;
    var timer = controller ? setTimeout(function () { controller.abort(); }, TIMEOUT_MS) : null;
    var headers = { accept: 'application/json' };
    if (opts.apiKey) headers['x-nxopen-api-key'] = opts.apiKey;

    return httpGet(buildUrl(opts.baseUrl, path, params), headers, controller ? controller.signal : undefined).then(function (res) {
      return res.text().then(function (text) {
        var body = null;
        try { body = text ? JSON.parse(text) : null; } catch (e) { body = null; }
        if (!res.ok) {
          var err = body && body.error;
          throw new ApiError(err && err.name || ('HTTP' + res.status), err && err.message, res.status);
        }
        return body;
      });
    }, function (e) {
      if (e && (e.name === 'AbortError' || /abort|timeout/i.test(e.message || ''))) throw new ApiError('TIMEOUT');
      throw new ApiError('NETWORK', e && e.message);
    }).finally(function () {
      if (timer) clearTimeout(timer);
    });
  }

  function request(opts, path, params) {
    var attempt = 0;
    function tryOnce() {
      return enqueue(function () { return fetchOnce(opts, path, params); }).catch(function (e) {
        // 호출 한도 초과는 잠시 기다렸다가 재시도
        var limited = e && (e.code === 'OPENAPI00007' || e.status === 429);
        if (limited && attempt < 2) {
          attempt++;
          return sleep(1500 * attempt).then(tryOnce);
        }
        throw e;
      });
    }
    return tryOnce();
  }

  function create(opts) {
    return {
      characterList: function () {
        return request(opts, 'character/list');
      },
      ocid: function (name) {
        return request(opts, 'id', { character_name: name }).then(function (b) { return b && b.ocid; });
      },
      basic: function (ocid) {
        return request(opts, 'character/basic', { ocid: ocid });
      },
      scheduler: function (ocid) {
        return request(opts, 'scheduler/character-state', { ocid: ocid });
      }
    };
  }

  root.MH = root.MH || {};
  root.MH.api = {
    create: create,
    ApiError: ApiError,
    DEFAULT_BASE: DEFAULT_BASE
  };
})(typeof window !== 'undefined' ? window : globalThis);
