#!/usr/bin/env node
/*
 * (선택) 로컬 프록시.
 * 배경화면에서 넥슨 API를 직접 호출할 때 "네트워크 오류 (CORS 차단)"가 뜨는 경우에만 사용한다.
 * 외부 패키지 없이 Node.js만 있으면 된다.
 *
 *   node proxy/maple-hud-proxy.js            # 127.0.0.1:17890
 *   NEXON_API_KEY=live_xxx node proxy/maple-hud-proxy.js 18000
 *
 * 배경화면 속성 "[API] 프록시 주소"에 http://127.0.0.1:17890 을 입력한다.
 * NEXON_API_KEY를 지정하면 배경화면 속성에는 API 키를 비워둬도 된다.
 */
'use strict';

const http = require('http');
const https = require('https');

const PORT = Number(process.argv[2] || process.env.PORT || 17890);
const API_KEY = process.env.NEXON_API_KEY || '';
const UPSTREAM = 'open.api.nexon.com';

const CORS = {
  'access-control-allow-origin': '*',
  'access-control-allow-methods': 'GET, OPTIONS',
  'access-control-allow-headers': 'x-nxopen-api-key, accept',
  // file:// 페이지에서 localhost로 보내는 요청을 Chromium이 막지 않도록
  'access-control-allow-private-network': 'true'
};

function sendError(res, status, message) {
  res.writeHead(status, Object.assign({ 'content-type': 'application/json; charset=utf-8' }, CORS));
  res.end(JSON.stringify({ error: { name: 'PROXY', message: message } }));
}

const server = http.createServer((req, res) => {
  if (req.method === 'OPTIONS') {
    res.writeHead(204, CORS);
    res.end();
    return;
  }
  if (req.method !== 'GET' || !req.url.startsWith('/maplestory/')) {
    sendError(res, 404, 'only /maplestory/* GET requests are proxied');
    return;
  }

  const key = req.headers['x-nxopen-api-key'] || API_KEY;
  if (!key) {
    sendError(res, 400, 'API 키가 없습니다. 배경화면 설정이나 NEXON_API_KEY 환경변수에 넣어주세요.');
    return;
  }

  const upstream = https.request({
    host: UPSTREAM,
    path: req.url,
    method: 'GET',
    headers: { 'x-nxopen-api-key': key, accept: 'application/json' },
    timeout: 15000
  }, (up) => {
    res.writeHead(up.statusCode || 502, Object.assign({
      'content-type': up.headers['content-type'] || 'application/json; charset=utf-8'
    }, CORS));
    up.pipe(res);
  });
  upstream.on('timeout', () => upstream.destroy(new Error('upstream timeout')));
  upstream.on('error', (e) => {
    if (!res.headersSent) sendError(res, 502, e.message);
    else res.end();
  });
  upstream.end();
});

server.listen(PORT, '127.0.0.1', () => {
  console.log(`maple-hud proxy: http://127.0.0.1:${PORT} → https://${UPSTREAM}`);
  if (API_KEY) console.log('NEXON_API_KEY 환경변수의 키를 사용합니다.');
});
