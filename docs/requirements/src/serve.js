// Minimal static server for previewing generated documents and prototypes (dev only).
//   node serve.js [rootDir] [port]   — defaults: docs/requirements, 5150 (ports inside the LIS reserved range)
const http = require('http'), fs = require('fs'), path = require('path');
const root = path.resolve(process.argv[2] || path.join(__dirname, '..'));
const port = Number(process.argv[3] || process.env.PORT || 5150);
const types = { '.html': 'text/html; charset=utf-8', '.css': 'text/css', '.js': 'text/javascript', '.png': 'image/png', '.webp': 'image/webp', '.svg': 'image/svg+xml', '.pdf': 'application/pdf' };
http.createServer((req, res) => {
  let url = decodeURIComponent(req.url.split('?')[0]);
  if (url === '/') { const first = fs.readdirSync(root).find(f => f.endsWith('.html')); url = '/' + (first || 'index.html'); }
  const fp = path.normalize(path.join(root, url));
  if (!fp.startsWith(root)) { res.writeHead(403); return res.end('forbidden'); }
  fs.readFile(fp, (err, data) => {
    if (err) { res.writeHead(404); return res.end('not found'); }
    res.writeHead(200, { 'Content-Type': types[path.extname(fp)] || 'application/octet-stream' });
    res.end(data);
  });
}).listen(port, '127.0.0.1', () => console.log(`static preview of ${root} on http://127.0.0.1:${port}/`));
