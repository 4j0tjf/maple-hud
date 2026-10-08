const test = require('node:test');
const assert = require('node:assert/strict');

require('../wallpaper/js/config.js');
const config = globalThis.MH.config;

test('Wallpaper Engine 속성 값을 설정으로 변환한다', () => {
  const changes = [];
  config.onChange((keys, fromWE) => changes.push({ keys, fromWE }));

  globalThis.wallpaperPropertyListener.applyUserProperties({
    apikey: { value: 'test_key' },
    columns: { value: 2 },
    showall: { value: true },
    accentcolor: { value: '1 0 0.5' },
    bgimage: { value: 'C:\\Users\\me\\Pictures\\배경 #1.jpg' },
    unknownprop: { value: 'x' }
  });

  const cfg = config.get();
  assert.equal(cfg.apiKey, 'test_key');
  assert.equal(cfg.columns, 2);
  assert.equal(cfg.showAll, true);
  assert.equal(cfg.accent, '#ff0080');
  assert.equal(cfg.bgImage, 'file:///C:/Users/me/Pictures/%EB%B0%B0%EA%B2%BD%20%231.jpg');
  assert.equal(config.isWE(), true);
  assert.equal(changes.length, 1);
  assert.equal(changes[0].fromWE, true);
  assert.deepEqual(changes[0].keys.sort(), ['accent', 'apiKey', 'bgImage', 'columns', 'showAll']);

  // 바뀐 값이 없으면 알리지 않는다
  globalThis.wallpaperPropertyListener.applyUserProperties({ columns: { value: 2 } });
  assert.equal(changes.length, 1);
});

test('색상과 파일 경로 변환', () => {
  assert.equal(config.parseColor('#12abEF', '#000000'), '#12abEF');
  assert.equal(config.parseColor('12abef', '#000000'), '#12abef');
  assert.equal(config.parseColor('nope', '#000000'), '#000000');
  assert.equal(config.toFileUrl(''), '');
  assert.equal(config.toFileUrl('https://example.com/a.png'), 'https://example.com/a.png');
  assert.equal(config.toFileUrl('file:///D:/bg.webm'), 'file:///D:/bg.webm');
  assert.equal(config.toFileUrl('D:/wall/bg.webm'), 'file:///D:/wall/bg.webm');
  assert.equal(config.toFileUrl('bg/sky.jpg'), 'bg/sky.jpg');
});
