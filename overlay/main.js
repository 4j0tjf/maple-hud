/*
 * Maple Scheduler HUD 오버레이 앱.
 *
 * Wallpaper Engine 배경화면은 그대로 두고, 그 위에 투명한 창으로 HUD만 띄운다.
 * (바탕화면 아이콘 · 다른 창과 같은 층이라 장면/동영상/웹 어떤 배경화면이든 효과가 유지된다)
 * 화면은 ../wallpaper 의 웹 페이지를 그대로 쓴다.
 *
 * - 창은 작업 영역 전체를 덮지만 HUD 패널 밖에서는 클릭이 아래로 통과한다.
 * - 평소에는 포커스를 받지 않아서 HUD를 클릭해도 다른 창 위로 튀어나오지 않는다.
 * - 넥슨 API 호출은 메인 프로세스가 대신 해서 CORS 제한을 받지 않는다.
 */
'use strict';

const { app, BrowserWindow, Tray, Menu, screen, ipcMain, nativeImage, net } = require('electron');
const path = require('path');
const fs = require('fs');

const WEB_DIR = app.isPackaged
  ? path.join(process.resourcesPath, 'wallpaper')
  : path.join(__dirname, '..', 'wallpaper');
const ICON = path.join(__dirname, 'icon.png');
const HTTP_TIMEOUT_MS = 15000;

let win = null;
let tray = null;
let prefsFile = null;
let prefs = { displayId: null, alwaysOnTop: false };

/* ---------- 앱 설정 (모니터, 항상 위) ---------- */

function loadPrefs() {
  try {
    prefs = Object.assign(prefs, JSON.parse(fs.readFileSync(prefsFile, 'utf8')));
  } catch (e) {
    // 처음 실행
  }
}

function savePrefs() {
  try {
    fs.writeFileSync(prefsFile, JSON.stringify(prefs, null, 2));
  } catch (e) {
    console.error('설정 저장 실패', e);
  }
}

/* ---------- 창 ---------- */

function targetDisplay() {
  return screen.getAllDisplays().find((d) => d.id === prefs.displayId) || screen.getPrimaryDisplay();
}

function fitToDisplay() {
  if (win) win.setBounds(targetDisplay().workArea);
}

function bringToFront() {
  if (!win) return;
  win.showInactive();
  win.moveTop();
}

function send(command) {
  if (win) win.webContents.send('overlay:command', command);
}

function createWindow() {
  win = new BrowserWindow({
    ...targetDisplay().workArea,
    transparent: true,
    backgroundColor: '#00000000',
    frame: false,
    hasShadow: false,
    resizable: false,
    movable: false,
    minimizable: false,
    maximizable: false,
    fullscreenable: false,
    skipTaskbar: true,
    focusable: false,
    alwaysOnTop: prefs.alwaysOnTop,
    show: false,
    title: 'Maple Scheduler HUD',
    icon: ICON,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      // 맞춤법 검사 사전을 불러오지 않는다 (메모리 절약)
      spellcheck: false
    }
  });

  win.setIgnoreMouseEvents(true, { forward: true });
  win.loadFile(path.join(WEB_DIR, 'index.html'));
  win.once('ready-to-show', () => win.showInactive());

  // "바탕화면 보기" 등으로 최소화되면 다시 띄운다
  win.on('minimize', () => setTimeout(() => win && win.restore(), 150));
  win.on('closed', () => { win = null; });

  // 로컬 페이지만 띄운다
  win.webContents.setWindowOpenHandler(() => ({ action: 'deny' }));
  win.webContents.on('will-navigate', (e) => e.preventDefault());
}

/* ---------- 트레이 ---------- */

function exePath() {
  // portable exe는 실행할 때마다 임시 폴더에 풀리므로 원래 exe 경로를 등록해야 한다
  return process.env.PORTABLE_EXECUTABLE_FILE || process.execPath;
}

function autoStartEnabled() {
  return app.getLoginItemSettings({ path: exePath() }).openAtLogin;
}

function buildMenu() {
  const primary = screen.getPrimaryDisplay();
  const current = targetDisplay();
  const displays = screen.getAllDisplays().map((d, i) => ({
    label: `모니터 ${i + 1} (${d.size.width}×${d.size.height})${d.id === primary.id ? ' · 주 모니터' : ''}`,
    type: 'radio',
    checked: d.id === current.id,
    click: () => {
      prefs.displayId = d.id;
      savePrefs();
      fitToDisplay();
    }
  }));

  return Menu.buildFromTemplate([
    { label: 'Maple Scheduler HUD', enabled: false },
    { type: 'separator' },
    { label: '지금 새로고침', click: () => send('refresh') },
    { label: '설정…', click: () => { bringToFront(); send('settings'); } },
    { label: '맨 앞으로 가져오기', click: bringToFront },
    { type: 'separator' },
    { label: '표시할 모니터', submenu: displays },
    {
      label: '항상 다른 창 위에 표시',
      type: 'checkbox',
      checked: prefs.alwaysOnTop,
      click: (item) => {
        prefs.alwaysOnTop = item.checked;
        savePrefs();
        if (win) win.setAlwaysOnTop(item.checked);
      }
    },
    {
      label: 'Windows 시작 시 자동 실행',
      type: 'checkbox',
      enabled: app.isPackaged,
      checked: app.isPackaged && autoStartEnabled(),
      click: (item) => app.setLoginItemSettings({ openAtLogin: item.checked, path: exePath() })
    },
    { type: 'separator' },
    { label: '종료', click: () => app.quit() }
  ]);
}

function refreshMenu() {
  if (tray) tray.setContextMenu(buildMenu());
}

function createTray() {
  tray = new Tray(nativeImage.createFromPath(ICON).resize({ width: 16, height: 16 }));
  tray.setToolTip('Maple Scheduler HUD');
  tray.on('click', bringToFront);
  refreshMenu();
}

/* ---------- 렌더러와 통신 ---------- */

function fromOurWindow(e) {
  return win && e.sender === win.webContents;
}

// 넥슨 API(또는 로컬 프록시)의 /maplestory/ 경로만 허용한다
async function httpGet(url, headers) {
  const u = new URL(url);
  const local = u.protocol === 'http:' && (u.hostname === '127.0.0.1' || u.hostname === 'localhost');
  if ((u.protocol !== 'https:' && !local) || !u.pathname.startsWith('/maplestory/')) {
    throw new Error('허용되지 않은 주소입니다: ' + u.origin + u.pathname);
  }
  const allowed = {};
  ['accept', 'x-nxopen-api-key'].forEach((k) => {
    if (headers && headers[k]) allowed[k] = String(headers[k]);
  });

  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), HTTP_TIMEOUT_MS);
  try {
    const res = await net.fetch(u.toString(), { headers: allowed, signal: controller.signal });
    return { status: res.status, body: await res.text() };
  } finally {
    clearTimeout(timer);
  }
}

function setupIpc() {
  ipcMain.on('overlay:interactive', (e, on) => {
    if (fromOurWindow(e)) win.setIgnoreMouseEvents(!on, { forward: true });
  });

  // 설정창을 여는 동안만 키보드 입력을 받는다
  ipcMain.on('overlay:focusable', (e, on) => {
    if (!fromOurWindow(e)) return;
    win.setFocusable(!!on);
    if (on) {
      win.setIgnoreMouseEvents(false);
      win.focus();
    } else {
      win.blur();
    }
  });

  ipcMain.handle('overlay:http-get', (e, url, headers) => {
    if (!fromOurWindow(e)) throw new Error('unknown sender');
    return httpGet(url, headers);
  });
}

/* ---------- 시작 ---------- */

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', bringToFront);

  app.whenReady().then(() => {
    app.setAppUserModelId('io.github.maplehud.overlay');
    prefsFile = path.join(app.getPath('userData'), 'overlay.json');
    loadPrefs();
    Menu.setApplicationMenu(null);
    setupIpc();
    createWindow();
    createTray();

    const onDisplaysChanged = () => {
      fitToDisplay();
      refreshMenu();
    };
    screen.on('display-added', onDisplaysChanged);
    screen.on('display-removed', onDisplaysChanged);
    screen.on('display-metrics-changed', onDisplaysChanged);
  });

  app.on('window-all-closed', () => app.quit());
}
