/*
 * Electron 오버레이 앱(overlay/)에서만 동작한다.
 * - 창은 화면 전체를 덮는 투명 창이라, HUD 패널 밖에서는 클릭이 아래(바탕화면·다른 창)로 통과하게 한다.
 * - 트레이 메뉴 명령(새로고침, 설정)을 받는다.
 */
(function (root) {
  'use strict';

  var bridge = root.mapleOverlay;
  if (!bridge) return;
  var doc = root.document;
  var interactive = false;

  function setInteractive(on) {
    if (on === interactive) return;
    interactive = on;
    bridge.setInteractive(on);
  }

  // 클릭을 통과시키는 동안에도 마우스 이동은 전달되므로, 포인터가 패널 위에 있을 때만 클릭을 받는다
  doc.addEventListener('mousemove', function (e) {
    var target = doc.elementFromPoint(e.clientX, e.clientY);
    setInteractive(!!(target && target.closest('#hud, #settings:not([hidden])')));
  });
  doc.addEventListener('mouseleave', function () { setInteractive(false); });

  bridge.onCommand(function (cmd) {
    if (cmd === 'refresh') root.MH.app.refresh();
    if (cmd === 'settings') root.MH.app.openSettings();
  });
})(window);
