// Builds the two self-contained Sky LIS prototype HTML files from the modular sources.
//   node prototypes/src/build.js
const fs = require('fs'), path = require('path');
const src = __dirname, dist = path.join(src, '..', 'dist');
fs.mkdirSync(dist, { recursive: true });
const read = f => fs.readFileSync(path.join(src, f), 'utf8');
// A literal "</script" inside JS would terminate the inline <script> element; escape it (valid inside JS strings).
const safe = js => js.split('</script').join('<\\/script');
const logo = 'data:image/webp;base64,' + fs.readFileSync(path.join(src, 'brand', 'sky-lis-logo.webp')).toString('base64');
const css = read('core.css');
const shared = safe(['i18n.js', 'core.js', 'data.js', 'screens.shared.js'].map(read).join('\n'));
const readApp = f => safe(Array.isArray(f) ? f.map(read).join('\n') : read(f));

const page = (title, appFile, scope) => `<!DOCTYPE html>
<html lang="en" dir="ltr">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">
<title>${title}</title>
<link rel="icon" href="${logo}">
<style>
${css}
</style>
</head>
<body>
<div id="auth"></div>
<div id="app" class="hidden"></div>
<div id="modalRoot" class="modal-root"></div>
<div id="toasts" class="toasts"></div>
<script>
window.LOGO_DATA = "${logo}";
window.SCOPE = "${scope}"; // "full" = complete v2/v3 scope · "r1" = release-1 scope (existing pages enhanced; client without new pages/modules)
${shared}
${readApp(appFile)}
</script>
</body>
</html>`;

const CLIENT = ['app.client.1.js', 'app.client.2.js', 'app.client.3.js', 'app.client.4.js'];
const outputs = [
  ['Sky-LIS-Client-Portal-Prototype-v2.html', 'Sky LIS — Client Portal Prototype v2 (full scope)', CLIENT, 'full'],
  ['Sky-LIS-Super-Admin-Portal-Prototype-v3.html', 'Sky LIS — Super Admin Portal Prototype v3 (full scope)', 'app.admin.js', 'full'],
  ['Sky-LIS-Client-Portal-Prototype-v2.1-Release1.html', 'Sky LIS — Client Portal Prototype v2.1 (release 1 scope)', CLIENT, 'r1'],
  ['Sky-LIS-Super-Admin-Portal-Prototype-v3.1-Release1.html', 'Sky LIS — Super Admin Portal Prototype v3.1 (release 1 scope)', 'app.admin.js', 'r1'],
];
for (const [file, title, app, scope] of outputs) {
  const html = page(title, app, scope);
  fs.writeFileSync(path.join(dist, file), html, 'utf8');
  console.log(`${file}: ${(html.length / 1024).toFixed(0)} KB`);
}
