#!/usr/bin/env node
// Serves a built SPA (a pinned release bundle) for a validation campaign, so edits in the working tree cannot reach the
// code under test the way the hot-reloading dev server lets them. Same contract as `ng serve` with a proxy config:
// static files with the Angular fallback to index.html, and /api, /health, /.well-known forwarded to the API.
//
//   node tests/validation/serve-ui.mjs <bundle folder> [port=4300] [api=http://127.0.0.1:5102]
//
// Binds 127.0.0.1 only. The Host header is forwarded unchanged (the API's origin check compares Origin with the host the
// browser used, exactly as through the dev proxy), so several host names (127.0.0.1, localhost) keep separate sessions.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';

const [bundleArg, portArg, apiArg] = process.argv.slice(2);
if (!bundleArg) {
  console.error('usage: node tests/validation/serve-ui.mjs <bundle folder> [port] [api origin]');
  process.exit(2);
}

const root = path.resolve(bundleArg);
const port = Number(portArg ?? 4300);
const api = new URL(apiArg ?? 'http://127.0.0.1:5102');
if (!fs.existsSync(path.join(root, 'index.html'))) {
  console.error(`no index.html in ${root}`);
  process.exit(2);
}

const TYPES = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8', '.json': 'application/json; charset=utf-8', '.svg': 'image/svg+xml',
  '.png': 'image/png', '.jpg': 'image/jpeg', '.ico': 'image/x-icon', '.woff2': 'font/woff2', '.webp': 'image/webp',
  '.txt': 'text/plain; charset=utf-8', '.map': 'application/json; charset=utf-8',
};
const PROXIED = /^\/(api|health|\.well-known)(\/|$)/;

function proxy(req, res) {
  const upstream = http.request(
    { hostname: api.hostname, port: api.port, method: req.method, path: req.url, headers: req.headers },
    (answer) => {
      res.writeHead(answer.statusCode ?? 502, answer.headers);
      answer.pipe(res);
    },
  );
  upstream.on('error', () => {
    if (!res.headersSent) res.writeHead(502, { 'content-type': 'text/plain' });
    res.end('API unreachable');
  });
  req.pipe(upstream);
}

function serveFile(res, file, cacheControl) {
  res.writeHead(200, { 'content-type': TYPES[path.extname(file).toLowerCase()] ?? 'application/octet-stream', 'cache-control': cacheControl });
  fs.createReadStream(file).pipe(res);
}

const server = http.createServer((req, res) => {
  const url = new URL(req.url ?? '/', 'http://local');
  if (PROXIED.test(url.pathname)) {
    proxy(req, res);
    return;
  }

  const relative = decodeURIComponent(url.pathname).replace(/^\/+/, '');
  const candidate = path.resolve(root, relative);
  const inside = candidate === root || candidate.startsWith(root + path.sep);
  if (inside && relative !== '' && fs.existsSync(candidate) && fs.statSync(candidate).isFile()) {
    serveFile(res, candidate, 'no-cache');
    return;
  }

  serveFile(res, path.join(root, 'index.html'), 'no-store');
});

server.listen(port, '127.0.0.1', () => {
  console.log(`serving ${root} on http://127.0.0.1:${port} (API ${api.origin})`);
});
