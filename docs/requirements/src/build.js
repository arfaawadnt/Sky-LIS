// Builds docs/requirements/LIS_Requirements_Specification.html from requirements.data.js (static HTML, no runtime JS needed).
const fs = require('fs');
const path = require('path');
const D = require('./requirements.data.js');

const esc = s => String(s ?? '').replace(/[&<>"]/g, m => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[m]));
const PRI = { M: ['Must', 'pri-m'], S: ['Should', 'pri-s'], C: ['Could', 'pri-c'] };
const ORG = { Both: ['Both', 'org-both'], LDM: ['LDM', 'org-ldm'], Sky: ['Sky', 'org-sky'], Proposed: ['Proposed', 'org-prop'] };
const badge = (map, k) => { const [t, c] = map[k] || [k, '']; return `<span class="badge ${c}">${esc(t)}</span>`; };

// ---- statistics ----
const allReqs = D.sections.flatMap(s => s.reqs.map(r => ({ sec: s.id, id: r[0], pri: r[3], org: r[4] })));
const count = (arr, key, val) => arr.filter(x => x[key] === val).length;
const stats = {
  total: allReqs.length,
  M: count(allReqs, 'pri', 'M'), S: count(allReqs, 'pri', 'S'), C: count(allReqs, 'pri', 'C'),
  Both: count(allReqs, 'org', 'Both'), LDM: count(allReqs, 'org', 'LDM'), Sky: count(allReqs, 'org', 'Sky'), Proposed: count(allReqs, 'org', 'Proposed'),
  nfr: D.nfr.length, rules: D.rules.length, oq: D.openQuestions.length
};

// ---- numbering ----
let n = 0; const num = () => ++n;
const toc = [];
const h2 = (title, id) => { const k = num(); toc.push([k, title, id, 2]); return `<h2 id="${id}"><span class="num">${k}.</span> ${esc(title)}</h2>`; };
let sub = 0;
const h3 = (title, id) => { sub++; toc.push([`${n}.${sub}`, title, id, 3]); return `<h3 id="${id}"><span class="num">${n}.${sub}</span> ${esc(title)}</h3>`; };

const reqTable = (reqs) => `
<table class="req">
<thead><tr><th class="c-id">ID</th><th class="c-title">Requirement</th><th class="c-text">Statement</th><th class="c-pri">Priority</th><th class="c-org">Origin</th><th class="c-trace">Trace</th></tr></thead>
<tbody>${reqs.map(([id, t, s, p, o, tr]) => `
<tr id="${id}"><td class="c-id"><a href="#${id}">${esc(id)}</a></td><td class="c-title">${esc(t)}</td><td class="c-text">${esc(s)}</td><td class="c-pri">${badge(PRI, p)}</td><td class="c-org">${badge(ORG, o)}</td><td class="c-trace">${esc(tr)}</td></tr>`).join('')}
</tbody></table>`;

const simpleTable = (heads, rows, cls = '') => `
<table class="${cls}">
<thead><tr>${heads.map(h => `<th>${esc(h)}</th>`).join('')}</tr></thead>
<tbody>${rows.map(r => `<tr>${r.map((c, i) => `<td>${i === 0 && /^[A-Z]{2,3}-\d/.test(c) ? `<strong>${esc(c)}</strong>` : esc(c)}</td>`).join('')}</tr>`).join('')}</tbody></table>`;

// ---- body ----
let body = '';
body += `<section class="cover">
  <div class="cover-kicker">${esc(D.meta.docId)} · Version ${esc(D.meta.version)} · ${esc(D.meta.status)}</div>
  <h1>${esc(D.meta.title)}</h1>
  <p class="cover-sub">${esc(D.meta.subtitle)}</p>
  <table class="meta">
    <tr><th>Document ID</th><td>${esc(D.meta.docId)}</td><th>Version</th><td>${esc(D.meta.version)}</td></tr>
    <tr><th>Status</th><td>${esc(D.meta.status)}</td><th>Date</th><td>${esc(D.meta.date)}</td></tr>
    <tr><th>Prepared for</th><td>${esc(D.meta.preparedFor)}</td><th>Prepared by</th><td>${esc(D.meta.preparedBy)}</td></tr>
    <tr><th>Classification</th><td colspan="3">${esc(D.meta.classification)}</td></tr>
  </table>
  <div class="stats">
    <div class="stat"><strong>${stats.total}</strong><span>Functional requirements</span></div>
    <div class="stat"><strong>${stats.nfr}</strong><span>Non-functional requirements</span></div>
    <div class="stat"><strong>${stats.rules}</strong><span>Business rules</span></div>
    <div class="stat"><strong>${stats.oq}</strong><span>Open questions</span></div>
  </div>
  <div class="stats small">
    <div class="stat"><strong>${stats.M}</strong><span>Must</span></div>
    <div class="stat"><strong>${stats.S}</strong><span>Should</span></div>
    <div class="stat"><strong>${stats.C}</strong><span>Could</span></div>
    <div class="stat"><strong>${stats.Both}</strong><span>Origin: both</span></div>
    <div class="stat"><strong>${stats.LDM}</strong><span>Origin: LDM only</span></div>
    <div class="stat"><strong>${stats.Sky}</strong><span>Origin: Sky only</span></div>
    <div class="stat"><strong>${stats.Proposed}</strong><span>Origin: proposed</span></div>
  </div>
</section>`;

body += `<section>${h2('Introduction', 'intro')}
${(sub = 0, h3('Purpose', 'purpose'))}<p>${esc(D.intro.purpose)}</p>
${h3('Scope', 'scope')}<p>${esc(D.intro.scope)}</p>
${h3('Audience', 'audience')}<p>${esc(D.intro.audience)}</p>
${h3('Source documents', 'sources')}${simpleTable(['Source', 'Location'], D.meta.sources)}
${h3('Conventions', 'conventions')}${simpleTable(['Convention', 'Meaning'], D.intro.conventions)}
<div class="legend"><span>Priority:</span> ${badge(PRI, 'M')} ${badge(PRI, 'S')} ${badge(PRI, 'C')} <span class="gap"></span><span>Origin:</span> ${badge(ORG, 'Both')} ${badge(ORG, 'LDM')} ${badge(ORG, 'Sky')} ${badge(ORG, 'Proposed')}</div>
</section>`;

body += `<section>${h2('Product overview', 'overview')}
${(sub = 0, h3('Vision', 'vision'))}
<p>The new LIS is a cloud, subscription-based, multi-tenant laboratory information system. National Technology operates a <strong>Super Admin portal</strong> for client lifecycle, internal users and global master data; each subscribed laboratory runs its daily work in its own <strong>Client portal</strong> with branches, roles, catalogue copy, payers and price lists, treasuries and integrations. Functionally it must preserve everything laboratories rely on in LDM — sample lifecycle, result semantics, status-driven queues, contract pricing, cash closing, claims, analyser interfacing and auto-verification — while adopting the prototypes' modern platform, security and user experience.</p>
${h3('Actors', 'actors')}${simpleTable(['Actor', 'Responsibilities'], D.actors)}
${h3('Domain entities (overview)', 'entities')}
<div class="entity-grid">${D.entities.flat().map(e => `<span class="entity">${esc(e)}</span>`).join('')}</div>
<p class="note">Relationships at a glance: a tenant has branches, users, roles and a master-data copy · a patient has visits · a visit has order lines, samples, payments and documents · a sample carries status events and labels · an order line yields results with logs, flags, comments and attachments · a payer has contract tiers with price lists derived from base lists · an analyser has mappings and interface logs.</p>
</section>`;

body += `<section>${h2('Functional requirements', 'functional')}`;
sub = 0;
for (const s of D.sections) {
  body += `${h3(`${s.title} (${s.id})`, 'sec-' + s.id)}<p class="intro">${esc(s.intro)}</p>${reqTable(s.reqs)}`;
}
body += `</section>`;

body += `<section>${h2('Non-functional requirements', 'nfr')}
<table class="req">
<thead><tr><th class="c-id">ID</th><th class="c-title">Quality</th><th class="c-text">Statement</th><th class="c-pri">Priority</th><th class="c-org">Origin</th></tr></thead>
<tbody>${D.nfr.map(([id, t, s, p, o]) => `<tr id="${id}"><td class="c-id"><a href="#${id}">${esc(id)}</a></td><td class="c-title">${esc(t)}</td><td class="c-text">${esc(s)}</td><td class="c-pri">${badge(PRI, p)}</td><td class="c-org">${badge(ORG, o)}</td></tr>`).join('')}</tbody></table>
</section>`;

body += `<section>${h2('Business rules', 'rules')}
<p class="intro">Rules that must hold regardless of screen. Most are proven in LDM operation; they are listed so that no implementation choice can accidentally break them.</p>
${simpleTable(['ID', 'Rule', 'Statement', 'Source'], D.rules, 'rules')}
</section>`;

body += `<section>${h2('Gap register: Sky LIS prototypes versus LDM', 'gaps')}
${(sub = 0, h3('Matching — the shared core', 'gap-match'))}${simpleTable(['Area', 'What both already cover'], D.gaps.matching)}
${h3('Extra in Sky LIS — new platform capabilities', 'gap-extra')}${simpleTable(['Area', 'What the prototypes add beyond LDM'], D.gaps.extra)}
${h3('Missing in the prototypes — LDM capabilities to carry forward', 'gap-missing')}${simpleTable(['Area', 'What LDM has and the prototypes lack', 'Proposed disposition'], D.gaps.missing)}
<p class="note">The prototypes are user-interface mock-ups built from an SRS that was not available for this analysis; a "missing" item may be out of the prototype's scope rather than out of the product's. The disposition column is a recommendation, not a decision.</p>
</section>`;

body += `<section>${h2('Open questions', 'questions')}
${simpleTable(['ID', 'Question', 'Owner'], D.openQuestions)}
</section>`;

body += `<section>${h2('Appendices', 'appendix')}
${(sub = 0, h3('Glossary', 'glossary'))}${simpleTable(['Term', 'Definition'], D.glossary)}
${h3('Traceability by module', 'trace')}${simpleTable(['Module', 'Sky LIS prototype screens', 'LDM manual references'], D.traceability)}
${h3('Terminology map — Sky LIS ↔ LDM', 'termmap')}${simpleTable(['Sky LIS', 'LDM'], [
  ['Client (tenant)', 'Licensed installation / site'], ['Clinical Group', 'Group / unit'], ['Service (Test type)', 'Single test or Profile'], ['Package', 'Mega profile / Panel'],
  ['Test', 'Single test / parameter'], ['Print Alone Service', 'Mega profile printed as its own report'], ['Payer / Contract', 'Rank / Relative'], ['Pricelist', 'Rank Fees tier price list'],
  ['Query Service', 'Financial ▸ Query'], ['Preparations', 'Preparation questions / precautions'], ['Comments master', 'Test Comments / comment library'], ['Sample Collected Outside', 'Lab-to-Lab "Sample Collected Inside the Lab" (inverse)'],
  ['Cancel Service', 'Delete Patient (cancel visit/tests)'], ['Refund', 'Return Money'], ['Treasury', 'Cash drawer (Income ▸ Total Daily Income)'], ['Previous Results / Attachments / Logs', 'Last Result / ATT / RL + SL'],
  ['Roles module access', 'Groups And Privileges'], ['LOINC Code', 'Unique Services Codes ▸ LOINC']
])}
${h3('Revision history', 'history')}${simpleTable(['Version', 'Date', 'Author', 'Change'], [[D.meta.version, D.meta.date, 'Claude Code', 'First draft consolidated from the LDM knowledge base and the two Sky LIS prototypes.']])}
</section>`;

// ---- TOC ----
const tocHtml = `<nav class="toc"><div class="toc-title">Contents</div><ol>${toc.map(([k, t, id, lvl]) => `<li class="lvl${lvl}"><a href="#${id}"><span class="num">${k}</span> ${esc(t)}</a></li>`).join('')}</ol></nav>`;

const css = `
:root{--ink:#1a2330;--ink-2:#44515f;--muted:#6b7784;--line:#dfe4ea;--line-soft:#edf0f4;--bg:#f5f7f9;--paper:#fff;--accent:#0f4c81;--accent-2:#0b3a63;--accent-soft:#e8f0f8;
--m:#9b2335;--m-soft:#fbe9ec;--s:#9a6200;--s-soft:#fff4df;--c:#2f6f4e;--c-soft:#e6f4ec;--both:#1d5c8f;--both-soft:#e3eef8;--ldm:#6a3fa0;--ldm-soft:#efe8f8;--sky:#0e7490;--sky-soft:#e0f4f8;--prop:#5f6b76;--prop-soft:#eceff2}
*{box-sizing:border-box}html{scroll-behavior:smooth}
body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.6 "Segoe UI",Inter,system-ui,-apple-system,Roboto,Arial,sans-serif}
.page{max-width:1240px;margin:0 auto;padding:32px 24px 80px}
.paper{background:var(--paper);border:1px solid var(--line);border-radius:12px;box-shadow:0 1px 2px rgba(0,0,0,.04),0 12px 32px -20px rgba(0,0,0,.25);padding:48px 56px}
.topbar{display:flex;justify-content:space-between;align-items:center;font-size:12px;color:var(--muted);padding:0 4px 14px}
.topbar b{color:var(--ink-2)}
.cover{border-bottom:2px solid var(--accent);padding-bottom:28px;margin-bottom:32px}
.cover-kicker{font-size:12px;letter-spacing:.08em;text-transform:uppercase;color:var(--accent);font-weight:600}
h1{font-size:34px;line-height:1.2;margin:8px 0 6px;color:var(--accent-2);letter-spacing:-.01em}
.cover-sub{font-size:16px;color:var(--ink-2);max-width:60rem;margin:0 0 22px}
table{border-collapse:collapse;width:100%;font-size:13.5px;margin:12px 0 22px}
th,td{border:1px solid var(--line);padding:8px 10px;vertical-align:top;text-align:left}
thead th{background:var(--accent-soft);color:var(--accent-2);font-weight:600;font-size:12.5px;letter-spacing:.02em}
tbody tr:nth-child(even) td{background:#fafbfc}
table.meta th{width:14%;background:var(--line-soft);font-weight:600;color:var(--ink-2)}table.meta td{width:36%}
.stats{display:flex;flex-wrap:wrap;gap:12px;margin-top:18px}.stats.small{margin-top:10px}
.stat{flex:1 1 140px;background:var(--accent-soft);border:1px solid #cfe0f0;border-radius:10px;padding:12px 14px;display:flex;flex-direction:column}
.stat strong{font-size:24px;color:var(--accent-2);line-height:1.1}.stat span{font-size:12px;color:var(--ink-2)}
.stats.small .stat{flex:1 1 110px;padding:9px 12px;background:#fff}.stats.small .stat strong{font-size:18px}
.toc{background:var(--line-soft);border:1px solid var(--line);border-radius:10px;padding:18px 22px;margin:0 0 34px}
.toc-title{font-weight:700;color:var(--accent-2);margin-bottom:8px;font-size:14px;text-transform:uppercase;letter-spacing:.06em}
.toc ol{list-style:none;margin:0;padding:0;columns:2;column-gap:32px}.toc li{break-inside:avoid;padding:2px 0;font-size:13.5px}
.toc li.lvl3{padding-left:22px;font-size:13px}.toc a{color:var(--ink);text-decoration:none}.toc a:hover{color:var(--accent)}.toc .num{display:inline-block;min-width:2.6em;color:var(--muted);font-variant-numeric:tabular-nums}
section{margin-bottom:40px}
h2{font-size:24px;color:var(--accent-2);border-bottom:1px solid var(--line);padding-bottom:8px;margin:44px 0 16px}
h3{font-size:17px;color:var(--ink);margin:30px 0 8px}
h2 .num,h3 .num{color:var(--accent);margin-right:6px;font-variant-numeric:tabular-nums}
p{margin:8px 0 12px;max-width:70rem}p.intro{color:var(--ink-2)}p.note{font-size:13px;color:var(--muted);border-left:3px solid var(--line);padding-left:12px}
.badge{display:inline-block;font-size:11.5px;font-weight:600;padding:2px 8px;border-radius:999px;white-space:nowrap;border:1px solid transparent}
.pri-m{background:var(--m-soft);color:var(--m);border-color:#efc4cb}.pri-s{background:var(--s-soft);color:var(--s);border-color:#f3dba6}.pri-c{background:var(--c-soft);color:var(--c);border-color:#bfe0cc}
.org-both{background:var(--both-soft);color:var(--both);border-color:#bcd5ec}.org-ldm{background:var(--ldm-soft);color:var(--ldm);border-color:#d6c6ec}.org-sky{background:var(--sky-soft);color:var(--sky);border-color:#b6e2ea}.org-prop{background:var(--prop-soft);color:var(--prop);border-color:#d3d9df}
.legend{display:flex;flex-wrap:wrap;align-items:center;gap:8px;font-size:13px;color:var(--ink-2);margin:6px 0 14px}.legend .gap{width:18px}
table.req .c-id{width:7%;white-space:nowrap}table.req .c-id a{color:var(--accent);text-decoration:none;font-weight:600;font-variant-numeric:tabular-nums}
table.req .c-title{width:15%;font-weight:600}table.req .c-text{width:50%}table.req .c-pri,table.req .c-org{width:7%;white-space:nowrap}table.req .c-trace{width:14%;font-size:12px;color:var(--muted)}
table.rules td:first-child{white-space:nowrap}
.entity-grid{display:flex;flex-wrap:wrap;gap:6px;margin:8px 0 12px}.entity{font-size:12.5px;background:var(--line-soft);border:1px solid var(--line);border-radius:6px;padding:3px 9px;color:var(--ink-2)}
tr:target td{background:#fff8e1!important;box-shadow:inset 3px 0 0 var(--s)}
.footer{margin-top:40px;padding-top:14px;border-top:1px solid var(--line);font-size:12px;color:var(--muted);display:flex;justify-content:space-between;flex-wrap:wrap;gap:8px}
@media (max-width:900px){.paper{padding:24px 18px}.toc ol{columns:1}h1{font-size:26px}table.req .c-trace{display:none}table.req thead .c-trace{display:none}table.req .c-text{width:auto}}
@media print{body{background:#fff;font-size:11.5px}.page{max-width:none;padding:0}.paper{border:0;box-shadow:none;border-radius:0;padding:0}.topbar{display:none}
h2{break-before:page;margin-top:0}.cover+section h2,section:first-of-type h2{break-before:auto}table{break-inside:auto}tr{break-inside:avoid}thead{display:table-header-group}
.toc ol{columns:2}.stat{border-color:#999}a{color:inherit}.badge{border:1px solid #888}@page{size:A4;margin:16mm 14mm}}
`;

const html = `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>${esc(D.meta.title)} — ${esc(D.meta.docId)} v${esc(D.meta.version)}</title>
<meta name="description" content="${esc(D.meta.subtitle)}">
<style>${css}</style>
</head>
<body>
<div class="page">
<div class="topbar"><span><b>${esc(D.meta.preparedFor)}</b> · ${esc(D.meta.docId)}</span><span>Version ${esc(D.meta.version)} · ${esc(D.meta.status)} · ${esc(D.meta.date)}</span></div>
<div class="paper">
${body.split('<section>')[0]}
${tocHtml}
${body.split('<section>').slice(1).map(s => '<section>' + s).join('')}
<div class="footer"><span>${esc(D.meta.title)} · ${esc(D.meta.docId)} v${esc(D.meta.version)} · ${esc(D.meta.classification)}</span><span>Generated from docs/requirements/src/requirements.data.js on ${esc(D.meta.date)}</span></div>
</div>
</div>
</body>
</html>`;

const out = path.join(__dirname, '..', 'LIS_Requirements_Specification.html');
fs.writeFileSync(out, html, 'utf8');
console.log(`Wrote ${out} (${html.length} chars) — ${stats.total} FR, ${stats.nfr} NFR, ${stats.rules} rules, ${stats.oq} open questions`);
