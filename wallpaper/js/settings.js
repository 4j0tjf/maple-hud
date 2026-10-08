/*
 * 설정창 (브라우저 · 오버레이 앱용). Wallpaper Engine에서는 배경화면 속성 패널을 쓴다.
 * 항목은 config.js의 FIELDS에서 만든다. 배치·투명도 같은 화면 설정은 바꾸는 즉시 미리 보여주고,
 * API 키처럼 다시 동기화가 필요한 설정은 저장할 때 적용한다.
 */
(function (root) {
  'use strict';

  var MH = root.MH;
  var config = MH.config;
  var doc = root.document;
  var GROUPS = ['API', '표시', '배치', '패널', '배경'];

  var fields = [];
  var byKey = {};
  var snapshot = null;
  var hooks = {};

  function $(id) {
    return doc.getElementById(id);
  }

  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  function control(f) {
    var ui = f.ui;
    var name = ' name="' + f.key + '"';
    var range = ' min="' + (ui.min != null ? ui.min : '') + '" max="' + (ui.max != null ? ui.max : '') + '" step="' + (ui.step || 1) + '"';
    switch (ui.input) {
      case 'password':
        return '<input type="password" autocomplete="off" spellcheck="false"' + name + '>';
      case 'number':
        return '<input type="number"' + range + name + '>';
      case 'range':
        return '<span class="range"><input type="range"' + range + name + '><output></output></span>';
      case 'select':
        return '<select' + name + '>' + ui.options.map(function (o) {
          return '<option value="' + esc(o[0]) + '">' + esc(o[1]) + '</option>';
        }).join('') + '</select>';
      case 'color':
        return '<input type="color"' + name + '>';
      default:
        return '<input type="text" spellcheck="false"' + name + '>';
    }
  }

  function row(f) {
    if (f.type === 'bool') {
      return '<label class="check"><input type="checkbox" name="' + f.key + '"> ' + esc(f.ui.label) + '</label>';
    }
    return '<label><span>' + esc(f.ui.label) + '</span>' + control(f) + '</label>';
  }

  function build() {
    fields = config.uiFields(config.isOverlay());
    byKey = {};
    fields.forEach(function (f) { byKey[f.key] = f; });
    $('settings-fields').innerHTML = GROUPS.map(function (g) {
      var list = fields.filter(function (f) { return f.ui.group === g; });
      if (!list.length) return '';
      return '<fieldset><legend>' + esc(g) + '</legend>' + list.map(row).join('') + '</fieldset>';
    }).join('');
  }

  function el(key) {
    return $('settings-form').elements[key];
  }

  function syncOutput(input) {
    var out = input.parentNode && input.parentNode.querySelector('output');
    if (out) out.textContent = input.value;
  }

  function fill() {
    var cfg = config.get();
    fields.forEach(function (f) {
      var input = el(f.key);
      var v = cfg[f.cfgKey];
      if (input.type === 'checkbox') input.checked = !!v;
      else input.value = v == null ? '' : v;
      syncOutput(input);
    });
  }

  function read() {
    var values = {};
    fields.forEach(function (f) {
      var input = el(f.key);
      values[f.key] = input.type === 'checkbox' ? input.checked : String(input.value).trim();
    });
    return values;
  }

  // 화면 설정만 골라낸다 (API 관련 값은 저장할 때만 적용)
  function visualOnly(values) {
    var out = {};
    Object.keys(values).forEach(function (k) {
      if (byKey[k] && !byKey[k].ui.data) out[k] = values[k];
    });
    return out;
  }

  function open() {
    build();
    fill();
    snapshot = read();
    $('settings').hidden = false;
    if (hooks.onOpen) hooks.onOpen();
    var first = $('settings-form').querySelector('input, select');
    if (first) first.focus();
  }

  function close(restore) {
    if (restore && snapshot) config.set(visualOnly(snapshot));
    snapshot = null;
    $('settings').hidden = true;
    if (hooks.onClose) hooks.onClose();
  }

  function init(h) {
    hooks = h || {};
    var form = $('settings-form');

    form.addEventListener('input', function (e) {
      var f = byKey[e.target.name];
      if (!f) return;
      syncOutput(e.target);
      if (f.ui.data) return;
      var values = {};
      values[f.key] = e.target.type === 'checkbox' ? e.target.checked : e.target.value;
      config.set(values);
    });

    form.addEventListener('submit', function (e) {
      e.preventDefault();
      config.saveBrowserSettings(read());
      close(false);
    });

    $('settings-cancel').addEventListener('click', function () { close(true); });
    doc.addEventListener('keydown', function (e) {
      if (e.key === 'Escape' && !$('settings').hidden) close(true);
    });
  }

  MH.settings = { init: init, open: open, close: close };
})(window);
