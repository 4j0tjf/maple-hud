/*
 * 설정 관리.
 * 우선순위: 기본값 < (브라우저) 저장된 설정 < URL 파라미터 < Wallpaper Engine 사용자 속성
 * URL 파라미터와 WE 속성은 같은 키 이름을 쓴다. (예: ?apikey=...&alignx=left&columns=2)
 */
(function (root) {
  'use strict';

  // [WE 속성 키, 설정 키, 타입, 기본값]
  var FIELDS = [
    ['apikey', 'apiKey', 'text', ''],
    ['characters', 'characters', 'text', ''],
    ['minlevel', 'minLevel', 'num', 200],
    ['maxchars', 'maxChars', 'num', 8],
    ['refreshmin', 'refreshMin', 'num', 15],
    ['showall', 'showAll', 'bool', false],
    ['hidedone', 'hideDone', 'bool', false],
    ['collapsedone', 'collapseDone', 'bool', true],
    ['showavatar', 'showAvatar', 'bool', true],
    ['columns', 'columns', 'num', 1],
    ['cardwidth', 'cardWidth', 'num', 400],
    ['uiscale', 'scale', 'num', 100],
    ['alignx', 'alignX', 'combo', 'right'],
    ['aligny', 'alignY', 'combo', 'top'],
    ['offsetx', 'offsetX', 'num', 48],
    ['offsety', 'offsetY', 'num', 48],
    ['panelopacity', 'opacity', 'num', 55],
    ['panelblur', 'blur', 'num', 14],
    ['accentcolor', 'accent', 'color', '#ffb547'],
    ['bgtype', 'bgType', 'combo', 'default'],
    ['bgimage', 'bgImage', 'file', ''],
    ['bgvideo', 'bgVideo', 'file', ''],
    ['bgurl', 'bgUrl', 'file', ''],
    ['bgcolor', 'bgColor', 'color', '#141821'],
    ['bgfit', 'bgFit', 'combo', 'cover'],
    ['bgdim', 'bgDim', 'num', 0],
    ['apibase', 'baseUrl', 'text', ''],
    ['demomode', 'demo', 'bool', false]
  ];

  var byWeKey = {};
  var defaults = {};
  FIELDS.forEach(function (f) {
    byWeKey[f[0]] = f;
    defaults[f[1]] = f[3];
  });
  // URL에서 짧게 쓸 수 있는 별칭
  byWeKey.demo = byWeKey.demomode;

  function clamp01(v) {
    return Math.max(0, Math.min(1, v));
  }

  function toHex(n) {
    var s = Math.round(clamp01(n) * 255).toString(16);
    return s.length < 2 ? '0' + s : s;
  }

  // WE 색상("0.1 0.5 1")이나 "#rrggbb", "rrggbb"를 #rrggbb로
  function parseColor(v, fallback) {
    var s = String(v == null ? '' : v).trim();
    var parts = s.split(/\s+/);
    if (parts.length === 3 && parts.every(function (p) { return isFinite(Number(p)); })) {
      return '#' + parts.map(function (p) { return toHex(Number(p)); }).join('');
    }
    if (/^#?[0-9a-f]{6}$/i.test(s)) return s.charAt(0) === '#' ? s : '#' + s;
    return fallback;
  }

  // WE 파일 속성은 절대 경로로 들어온다. 웹에서 쓸 수 있는 URL로 바꾼다.
  function toFileUrl(v) {
    var p = String(v == null ? '' : v).trim();
    if (!p) return '';
    if (/^(https?|file|data|blob):/i.test(p)) return p;
    p = p.replace(/\\/g, '/');
    var encoded = encodeURI(p).replace(/#/g, '%23').replace(/\?/g, '%3F');
    if (/^[a-z]:\//i.test(p)) return 'file:///' + encoded;
    if (p.charAt(0) === '/') return 'file://' + encoded;
    return encoded;
  }

  function convert(field, raw) {
    var type = field[2];
    var fallback = field[3];
    if (raw && typeof raw === 'object' && 'value' in raw) raw = raw.value;
    switch (type) {
      case 'bool':
        if (typeof raw === 'boolean') return raw;
        return /^(1|true|yes|on)$/i.test(String(raw));
      case 'num':
        var n = Number(raw);
        return isFinite(n) ? n : fallback;
      case 'color':
        return parseColor(raw, fallback);
      case 'file':
        return toFileUrl(raw);
      default:
        return raw == null ? fallback : String(raw);
    }
  }

  var cfg = {};
  Object.keys(defaults).forEach(function (k) { cfg[k] = defaults[k]; });

  var listeners = [];
  var pauseListeners = [];
  var weReadyListeners = [];
  var isWE = typeof root.wallpaperRegisterAudioListener === 'function';
  var weReady = false;

  function apply(source, fromWE) {
    var changed = [];
    Object.keys(source || {}).forEach(function (key) {
      var field = byWeKey[key.toLowerCase()];
      if (!field) return;
      var value = convert(field, source[key]);
      if (cfg[field[1]] !== value) {
        cfg[field[1]] = value;
        changed.push(field[1]);
      }
    });
    if (changed.length) {
      listeners.forEach(function (fn) { fn(changed.slice(), fromWE); });
    }
    return changed;
  }

  // 브라우저에서 ⚙ 설정창으로 저장한 값
  var BROWSER_KEY = 'browserConfig';
  function loadBrowserSettings() {
    var saved = root.MH && root.MH.store && root.MH.store.get(BROWSER_KEY);
    if (saved) apply(saved, false);
  }

  function saveBrowserSettings(values) {
    var saved = (root.MH.store.get(BROWSER_KEY)) || {};
    Object.keys(values).forEach(function (k) { saved[k] = values[k]; });
    root.MH.store.set(BROWSER_KEY, saved);
    apply(values, false);
  }

  function loadUrlParams() {
    if (!root.location || !root.location.search) return;
    var params = {};
    root.location.search.slice(1).split('&').forEach(function (pair) {
      if (!pair) return;
      var i = pair.indexOf('=');
      var k = decodeURIComponent(i < 0 ? pair : pair.slice(0, i));
      var v = i < 0 ? 'true' : decodeURIComponent(pair.slice(i + 1).replace(/\+/g, ' '));
      params[k] = v;
    });
    apply(params, false);
  }

  // Wallpaper Engine 연동: 페이지 로드 시 한 번 전체 값, 이후에는 바뀐 값만 들어온다.
  root.wallpaperPropertyListener = {
    applyUserProperties: function (props) {
      isWE = true;
      apply(props, true);
      if (!weReady) {
        weReady = true;
        weReadyListeners.forEach(function (fn) { fn(); });
      }
    },
    setPaused: function (paused) {
      pauseListeners.forEach(function (fn) { fn(!!paused); });
    }
  };

  root.MH = root.MH || {};
  root.MH.config = {
    defaults: defaults,
    get: function () { return cfg; },
    isWE: function () { return isWE; },
    isWEReady: function () { return weReady; },
    onChange: function (fn) { listeners.push(fn); },
    onPause: function (fn) { pauseListeners.push(fn); },
    onWEReady: function (fn) { if (weReady) fn(); else weReadyListeners.push(fn); },
    loadBrowserSettings: loadBrowserSettings,
    saveBrowserSettings: saveBrowserSettings,
    loadUrlParams: loadUrlParams,
    toFileUrl: toFileUrl,
    parseColor: parseColor
  };
})(typeof window !== 'undefined' ? window : globalThis);
