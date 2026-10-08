/* 웹 페이지(wallpaper/index.html)에 오버레이 앱 기능을 window.mapleOverlay 로 열어준다 */
'use strict';

const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('mapleOverlay', {
  setInteractive: (on) => ipcRenderer.send('overlay:interactive', !!on),
  setFocusable: (on) => ipcRenderer.send('overlay:focusable', !!on),
  httpGet: (url, headers) => ipcRenderer.invoke('overlay:http-get', String(url), headers || {}),
  onCommand: (fn) => ipcRenderer.on('overlay:command', (_e, cmd) => fn(cmd))
});
