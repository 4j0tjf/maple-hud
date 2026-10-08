/*
 * 설정 관리.
 * 우선순위: 기본값 < (브라우저) 저장된 설정 < URL 파라미터 < Wallpaper Engine 사용자 속성
 * URL 파라미터와 WE 속성은 같은 키 이름을 쓴다. (예: ?apikey=...&alignx=left&columns=2)
 */
(function (root) {
  'use strict';

  var ALIGN_X = [['left', '왼쪽'], ['center', '가운데'], ['right', '오른쪽']];
  var ALIGN_Y = [['top', '위'], ['center', '가운데'], ['bottom', '아래']];

  /*
   * [WE 속성 키, 설정 키, 타입, 기본값, 설정창 정보]
   * 설정창 정보: group/label, input(range·number·password·select·color), data(바뀌면 다시 동기화).
   *             없으면 설정창에 나오지 않는다.
   */
  var FIELDS = [
    ['apikey', 'apiKey', 'text', '', { group: 'API', label: '넥슨 Open API 키', input: 'password', data: true }],
    ['characters', 'characters', 'text', '', { group: 'API', label: '캐릭터 이름 (쉼표로 구분, 비우면 계정에서 자동 선택)', data: true }],
    ['minlevel', 'minLevel', 'num', 200, { group: 'API', label: '자동 선택 최소 레벨', input: 'number', min: 0, max: 300, data: true }],
    ['maxchars', 'maxChars', 'num', 8, { group: 'API', label: '자동 선택 최대 캐릭터 수', input: 'number', min: 1, max: 20, data: true }],
    ['refreshmin', 'refreshMin', 'num', 15, { group: 'API', label: '갱신 주기 (분)', input: 'number', min: 5, max: 180 }],
    ['apibase', 'baseUrl', 'text', '', { group: 'API', label: '프록시 주소 (보통 비워두세요)', data: true }],
    ['demomode', 'demo', 'bool', false, { group: 'API', label: '데모 데이터로 보기', data: true }],
    ['showall', 'showAll', 'bool', false, { group: '표시', label: '스케줄러에 등록 안 된 항목도 표시' }],
    ['hidedone', 'hideDone', 'bool', false, { group: '표시', label: '완료한 항목 숨기기' }],
    ['collapsedone', 'collapseDone', 'bool', true, { group: '표시', label: '모두 완료한 캐릭터 접기' }],
    ['showavatar', 'showAvatar', 'bool', true, { group: '표시', label: '캐릭터 이미지·경험치 표시', data: true }],
    ['showseconds', 'showSeconds', 'bool', true, { group: '표시', label: '시계·카운트다운을 초 단위로 표시 (끄면 화면 갱신이 1분에 한 번)' }],
    ['columns', 'columns', 'num', 1, { group: '배치', label: '열 개수', input: 'range', min: 1, max: 4 }],
    ['cardwidth', 'cardWidth', 'num', 400, { group: '배치', label: '카드 너비 (px)', input: 'range', min: 280, max: 720, step: 10 }],
    ['uiscale', 'scale', 'num', 100, { group: '배치', label: '크기 (%)', input: 'range', min: 50, max: 250, step: 5 }],
    ['alignx', 'alignX', 'combo', 'right', { group: '배치', label: '가로 위치', input: 'select', options: ALIGN_X }],
    ['aligny', 'alignY', 'combo', 'top', { group: '배치', label: '세로 위치', input: 'select', options: ALIGN_Y }],
    ['offsetx', 'offsetX', 'num', 48, { group: '배치', label: '가로 여백 (px)', input: 'range', min: 0, max: 600, step: 2 }],
    ['offsety', 'offsetY', 'num', 48, { group: '배치', label: '세로 여백 (px)', input: 'range', min: 0, max: 600, step: 2 }],
    ['panelopacity', 'opacity', 'num', 55, { group: '패널', label: '배경 불투명도 (%)', input: 'range', min: 0, max: 100 }],
    ['panelblur', 'blur', 'num', 14, { group: '패널', label: '뒤 배경 흐림 (px)', input: 'range', min: 0, max: 40 }],
    ['accentcolor', 'accent', 'color', '#ffb547', { group: '패널', label: '강조 색상', input: 'color' }],
    ['bgtype', 'bgType', 'combo', 'default', { group: '배경', label: '종류', input: 'select',
      options: [['default', '기본 그라데이션'], ['image', '이미지'], ['video', '동영상'], ['color', '단색']] }],
    ['bgimage', 'bgImage', 'file', ''],
    ['bgvideo', 'bgVideo', 'file', ''],
    ['bgurl', 'bgUrl', 'file', '', { group: '배경', label: '이미지·동영상 URL 또는 파일 경로' }],
    ['bgfit', 'bgFit', 'combo', 'cover', { group: '배경', label: '맞춤', input: 'select',
      options: [['cover', '화면 채우기 (잘림)'], ['contain', '전체 보이기'], ['fill', '늘이기']] }],
    ['bgcolor', 'bgColor', 'color', '#141821', { group: '배경', label: '색상 (단색 / 여백)', input: 'color' }],
    ['bgdim', 'bgDim', 'num', 0, { group: '배경', label: '어둡게 (%)', input: 'range', min: 0, max: 90 }]
  ];

  var byWeKey = {};
  var defaults = {};
  var dataKeys = [];
  FIELDS.forEach(function (f) {
    byWeKey[f[0]] = f;
    defaults[f[1]] = f[3];
    if (f[4] && f[4].data) dataKeys.push(f[1]);
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

  // 설정창에 보여줄 항목 [{ key(WE 키), cfgKey, type, ui }]
  function uiFields() {
    return FIELDS.filter(function (f) {
      return !!f[4];
    }).map(function (f) {
      return { key: f[0], cfgKey: f[1], type: f[2], ui: f[4] };
    });
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
    set: function (values) { return apply(values, false); },
    uiFields: uiFields,
    dataKeys: dataKeys,
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
