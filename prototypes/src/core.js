/* core.js — Sky LIS prototype framework (shared by the Super Admin and Client portals) */
'use strict';
const App = { lang: 'en', portal: 'client', screen: 'home', params: {}, user: null, branch: null, nav: [], screens: {}, store: {}, collapsed: false, booted: false };
const T = window.T || { en: {}, ar: {} };
function t(k) { const d = T[App.lang] || {}; if (d[k] != null) return d[k]; if (T.en[k] != null) return T.en[k]; return String(k).replace(/_/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, c => c.toUpperCase()); }
const lab = l => l == null ? '' : (typeof l === 'string' && l.startsWith('=') ? l.slice(1) : t(l));
const dual = (en, ar) => (App.lang === 'ar' && ar) ? ar : en;
const L = (o, k = 'name') => !o ? '' : ((App.lang === 'ar' && o[k + 'Ar']) ? o[k + 'Ar'] : (o[k] ?? ''));
const esc = s => String(s ?? '').replace(/[&<>"']/g, m => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[m]));
const el = s => document.querySelector(s);
const els = s => [...document.querySelectorAll(s)];
const money = v => (App.currency || 'EGP') + ' ' + Number(v || 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const num = (v, d = 2) => Number(v || 0).toLocaleString('en-US', { minimumFractionDigits: d, maximumFractionDigits: d });
const pad = (n, w = 2) => String(n).padStart(w, '0');
const fmtDate = d => { if (!d) return '—'; const x = new Date(d); return `${pad(x.getDate())}/${pad(x.getMonth() + 1)}/${x.getFullYear()}`; };
const fmtDT = d => { if (!d) return '—'; const x = new Date(d); return `${fmtDate(x)} ${pad(x.getHours())}:${pad(x.getMinutes())}`; };
const NOW = () => new Date('2026-10-08T14:30:00');
const hoursBetween = (a, b) => (new Date(b) - new Date(a)) / 36e5;
const fmtDur = h => { if (h == null || isNaN(h)) return '—'; const s = h < 0 ? '-' : ''; h = Math.abs(h); const d = Math.floor(h / 24), hh = Math.floor(h % 24), m = Math.round((h * 60) % 60); return s + (d ? d + 'D ' : '') + (hh ? hh + 'H ' : '') + m + 'Min'; };
const uid = (p = '') => p + Math.random().toString(36).slice(2, 8).toUpperCase();
const ageOf = dob => { if (!dob) return null; const b = new Date(dob), n = NOW(); let y = n.getFullYear() - b.getFullYear(), m = n.getMonth() - b.getMonth(), d = n.getDate() - b.getDate(); if (d < 0) { m--; d += 30; } if (m < 0) { y--; m += 12; } return { y, m, d }; };
const ageText = dob => { const a = ageOf(dob); return a ? `${a.y}Y ${a.m}M ${a.d}D` : '—'; };

/* ---------- icons (Feather-style, inline SVG) ---------- */
const ICONS = {
  dot: '<circle cx="12" cy="12" r="3"/>',
  home: '<path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/><polyline points="9 22 9 12 15 12 15 22"/>',
  users: '<path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>',
  user: '<path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"/><circle cx="12" cy="7" r="4"/>',
  userplus: '<path d="M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="8.5" cy="7" r="4"/><line x1="20" y1="8" x2="20" y2="14"/><line x1="23" y1="11" x2="17" y2="11"/>',
  settings: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/>',
  search: '<circle cx="11" cy="11" r="8"/><line x1="21" y1="21" x2="16.65" y2="16.65"/>',
  plus: '<line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>',
  edit: '<path d="M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7"/><path d="M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z"/>',
  trash: '<polyline points="3 6 5 6 21 6"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/>',
  printer: '<polyline points="6 9 6 2 18 2 18 9"/><path d="M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2"/><rect x="6" y="14" width="12" height="8"/>',
  file: '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/><line x1="16" y1="13" x2="8" y2="13"/><line x1="16" y1="17" x2="8" y2="17"/>',
  dollar: '<line x1="12" y1="1" x2="12" y2="23"/><path d="M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6"/>',
  activity: '<polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/>',
  clipboard: '<path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/><rect x="8" y="2" width="8" height="4" rx="1" ry="1"/>',
  list: '<line x1="8" y1="6" x2="21" y2="6"/><line x1="8" y1="12" x2="21" y2="12"/><line x1="8" y1="18" x2="21" y2="18"/><line x1="3" y1="6" x2="3.01" y2="6"/><line x1="3" y1="12" x2="3.01" y2="12"/><line x1="3" y1="18" x2="3.01" y2="18"/>',
  grid: '<rect x="3" y="3" width="7" height="7"/><rect x="14" y="3" width="7" height="7"/><rect x="14" y="14" width="7" height="7"/><rect x="3" y="14" width="7" height="7"/>',
  layers: '<polygon points="12 2 2 7 12 12 22 7 12 2"/><polyline points="2 17 12 22 22 17"/><polyline points="2 12 12 17 22 12"/>',
  shield: '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>',
  globe: '<circle cx="12" cy="12" r="10"/><line x1="2" y1="12" x2="22" y2="12"/><path d="M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z"/>',
  pin: '<path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/><circle cx="12" cy="10" r="3"/>',
  tag: '<path d="M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z"/><line x1="7" y1="7" x2="7.01" y2="7"/>',
  bell: '<path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9"/><path d="M13.73 21a2 2 0 0 1-3.46 0"/>',
  logout: '<path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4"/><polyline points="16 17 21 12 16 7"/><line x1="21" y1="12" x2="9" y2="12"/>',
  chevdown: '<polyline points="6 9 12 15 18 9"/>', chevright: '<polyline points="9 18 15 12 9 6"/>',
  menu: '<line x1="3" y1="12" x2="21" y2="12"/><line x1="3" y1="6" x2="21" y2="6"/><line x1="3" y1="18" x2="21" y2="18"/>',
  x: '<line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/>',
  check: '<polyline points="20 6 9 17 4 12"/>',
  eye: '<path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>',
  refresh: '<polyline points="23 4 23 10 17 10"/><polyline points="1 20 1 14 7 14"/><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15"/>',
  download: '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/>',
  upload: '<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" y1="3" x2="12" y2="15"/>',
  cpu: '<rect x="4" y="4" width="16" height="16" rx="2" ry="2"/><rect x="9" y="9" width="6" height="6"/><line x1="9" y1="1" x2="9" y2="4"/><line x1="15" y1="1" x2="15" y2="4"/><line x1="9" y1="20" x2="9" y2="23"/><line x1="15" y1="20" x2="15" y2="23"/><line x1="20" y1="9" x2="23" y2="9"/><line x1="20" y1="14" x2="23" y2="14"/><line x1="1" y1="9" x2="4" y2="9"/><line x1="1" y1="14" x2="4" y2="14"/>',
  droplet: '<path d="M12 2.69l5.66 5.66a8 8 0 1 1-11.31 0z"/>',
  zap: '<polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/>',
  chart: '<line x1="12" y1="20" x2="12" y2="10"/><line x1="18" y1="20" x2="18" y2="4"/><line x1="6" y1="20" x2="6" y2="16"/>',
  calendar: '<rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/>',
  truck: '<rect x="1" y="3" width="15" height="13"/><polygon points="16 8 20 8 23 11 23 16 16 16 16 8"/><circle cx="5.5" cy="18.5" r="2.5"/><circle cx="18.5" cy="18.5" r="2.5"/>',
  scan: '<path d="M8 3H5a2 2 0 0 0-2 2v3m18 0V5a2 2 0 0 0-2-2h-3m0 18h3a2 2 0 0 0 2-2v-3M3 16v3a2 2 0 0 0 2 2h3"/><line x1="7" y1="12" x2="17" y2="12"/>',
  alert: '<path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/>',
  info: '<circle cx="12" cy="12" r="10"/><line x1="12" y1="16" x2="12" y2="12"/><line x1="12" y1="8" x2="12.01" y2="8"/>',
  flask: '<path d="M9 3h6"/><path d="M10 3v6L4.5 19a1.5 1.5 0 0 0 1.3 2.2h12.4a1.5 1.5 0 0 0 1.3-2.2L14 9V3"/>',
  tube: '<path d="M9 2v14a3 3 0 0 0 6 0V2"/><path d="M7 2h10"/><path d="M9 10h6"/>',
  briefcase: '<rect x="2" y="7" width="20" height="14" rx="2" ry="2"/><path d="M16 21V5a2 2 0 0 0-2-2h-4a2 2 0 0 0-2 2v16"/>',
  card: '<rect x="1" y="4" width="22" height="16" rx="2" ry="2"/><line x1="1" y1="10" x2="23" y2="10"/>',
  book: '<path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20"/><path d="M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z"/>',
  link: '<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/>',
  clock: '<circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>',
  help: '<circle cx="12" cy="12" r="10"/><path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3"/><line x1="12" y1="17" x2="12.01" y2="17"/>',
  lock: '<rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>',
  unlock: '<rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 9.9-1"/>',
  arrow: '<line x1="5" y1="12" x2="19" y2="12"/><polyline points="12 5 19 12 12 19"/>',
  undo: '<polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/>',
  repeat: '<polyline points="17 1 21 5 17 9"/><path d="M3 11V9a4 4 0 0 1 4-4h14"/><polyline points="7 23 3 19 7 15"/><path d="M21 13v2a4 4 0 0 1-4 4H3"/>',
  more: '<circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/><circle cx="5" cy="12" r="1"/>',
  filter: '<polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"/>',
  mic: '<path d="M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z"/><path d="M19 10v2a7 7 0 0 1-14 0v-2"/><line x1="12" y1="19" x2="12" y2="23"/><line x1="8" y1="23" x2="16" y2="23"/>',
  clip: '<path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48"/>',
  msg: '<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z"/>',
  hash: '<line x1="4" y1="9" x2="20" y2="9"/><line x1="4" y1="15" x2="20" y2="15"/><line x1="10" y1="3" x2="8" y2="21"/><line x1="16" y1="3" x2="14" y2="21"/>',
  box: '<path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/><polyline points="3.27 6.96 12 12.01 20.73 6.96"/><line x1="12" y1="22.08" x2="12" y2="12"/>',
  server: '<rect x="2" y="2" width="20" height="8" rx="2" ry="2"/><rect x="2" y="14" width="20" height="8" rx="2" ry="2"/><line x1="6" y1="6" x2="6.01" y2="6"/><line x1="6" y1="18" x2="6.01" y2="18"/>',
  send: '<line x1="22" y1="2" x2="11" y2="13"/><polygon points="22 2 15 22 11 13 2 9 22 2"/>',
  checkcircle: '<path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"/><polyline points="22 4 12 14.01 9 11.01"/>',
  slash: '<circle cx="12" cy="12" r="10"/><line x1="4.93" y1="4.93" x2="19.07" y2="19.07"/>',
  pie: '<path d="M21.21 15.89A10 10 0 1 1 8 2.83"/><path d="M22 12A10 10 0 0 0 12 2v10z"/>',
  trend: '<polyline points="23 6 13.5 15.5 8.5 10.5 1 18"/><polyline points="17 6 23 6 23 12"/>',
  sliders: '<line x1="4" y1="21" x2="4" y2="14"/><line x1="4" y1="10" x2="4" y2="3"/><line x1="12" y1="21" x2="12" y2="12"/><line x1="12" y1="8" x2="12" y2="3"/><line x1="20" y1="21" x2="20" y2="16"/><line x1="20" y1="12" x2="20" y2="3"/><line x1="1" y1="14" x2="7" y2="14"/><line x1="9" y1="8" x2="15" y2="8"/><line x1="17" y1="16" x2="23" y2="16"/>',
  monitor: '<rect x="2" y="3" width="20" height="14" rx="2" ry="2"/><line x1="8" y1="21" x2="16" y2="21"/><line x1="12" y1="17" x2="12" y2="21"/>',
  home2: '<path d="M3 9l9-7 9 7v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
  wallet: '<path d="M20 7H4a2 2 0 0 0-2 2v10a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2z"/><path d="M16 3H8a2 2 0 0 0-2 2v2h12V5a2 2 0 0 0-2-2z"/><circle cx="17" cy="14" r="1.5"/>',
  percent: '<line x1="19" y1="5" x2="5" y2="19"/><circle cx="6.5" cy="6.5" r="2.5"/><circle cx="17.5" cy="17.5" r="2.5"/>',
  star: '<polygon points="12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2"/>',
  phone: '<path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.96.37 1.9.72 2.81a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.91.35 1.85.59 2.81.72A2 2 0 0 1 22 16.92z"/>',
  mail: '<path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/>',
  save: '<path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><polyline points="17 21 17 13 7 13 7 21"/><polyline points="7 3 7 8 15 8"/>',
  bug: '<circle cx="12" cy="12" r="8"/><line x1="12" y1="4" x2="12" y2="20"/><line x1="4" y1="12" x2="20" y2="12"/>',
  key: '<path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"/>',
  copy: '<rect x="9" y="9" width="13" height="13" rx="2" ry="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/>',
  play: '<polygon points="5 3 19 12 5 21 5 3"/>',
  dna: '<path d="M4 4c6 6 10 6 16 0"/><path d="M4 20c6-6 10-6 16 0"/><path d="M4 12c6 6 10 6 16 0"/><path d="M4 12c6-6 10-6 16 0"/>',
};
const ic = (n, s = 16) => `<svg class="ic" width="${s}" height="${s}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${ICONS[n] || ICONS.dot}</svg>`;

/* ---------- badges & statuses ---------- */
const TONES = { Active: 'ok', Inactive: 'muted', Pending: 'warn', Approved: 'ok', Rejected: 'bad', Open: 'info', Completed: 'ok', Paid: 'ok', Partial: 'warn', Unpaid: 'bad', Ordered: 'muted', Collected: 'info', Received: 'violet', Complete: 'warn', Reviewed: 'info', Verified: 'ok', Printed: 'navy', Reserved: 'warn', Cancelled: 'bad', Delayed: 'bad', Running: 'ok', Stopped: 'bad', Error: 'bad', Draft: 'muted', Issued: 'ok', Locked: 'bad', Unlocked: 'ok', High: 'bad', Low: 'info', Normal: 'ok', Panic: 'panic', Booked: 'info', Converted: 'ok', Yes: 'ok', No: 'muted', Manual: 'muted', Subscription: 'violet', Valid: 'ok', Invalid: 'bad', Released: 'ok', Held: 'bad', Online: 'ok', Offline: 'bad', Sent: 'ok', Failed: 'bad', Read: 'ok' };
const badge = (text, tone) => `<span class="badge ${tone || 'muted'}">${esc(text)}</span>`;
const sb = s => { if (s === true) s = 'Yes'; if (s === false) s = 'No'; const k = 'st_' + s; return badge(T.en[k] != null ? t(k) : s, TONES[s] || 'muted'); };
const flagBadge = f => !f || f === 'N' ? badge(t('fl_N'), 'ok') : f === 'H' ? badge(t('fl_H'), 'bad') : f === 'L' ? badge(t('fl_L'), 'info') : f === 'HH' ? badge(t('fl_HH'), 'panic') : f === 'LL' ? badge(t('fl_LL'), 'panic') : badge(f, 'warn');

/* ---------- layout helpers ---------- */
function pageHead(title, crumb, actions = '') { return `<div class="page-head"><div><h1>${title}</h1><div class="crumb">${crumb || ''}</div></div><div class="page-actions">${actions}</div></div>`; }
function card(o) { return `<div class="card ${o.cls || ''}">${o.title != null ? `<div class="card-head"><div><div class="card-title">${o.title}</div>${o.sub ? `<div class="card-sub">${o.sub}</div>` : ''}</div><div class="row">${o.actions || ''}</div></div>` : ''}<div class="card-body ${o.flush ? 'flush' : ''}" ${o.flush ? 'style="padding:0"' : ''}>${o.body || ''}</div></div>`; }
function kpi(label, value, hint, icon, tone = '') { return `<div class="card kpi ${tone}"><div><div class="lbl">${label}</div><div class="val">${value}</div><div class="hint">${hint || ''}</div></div><div class="kico">${ic(icon || 'activity', 18)}</div></div>`; }
function barChart(values, labels) { const max = Math.max(...values, 1); return `<div class="bar-chart">${values.map((v, i) => `<div class="bar-col"><b>${v}</b><div class="bar" style="height:${Math.round(v / max * 100)}%"></div><span>${esc(labels[i])}</span></div>`).join('')}</div>`; }
function toast(msg, type = 'ok', sub = '') { const w = el('#toasts'); const x = document.createElement('div'); x.className = 'toast ' + type; x.innerHTML = `${ic(type === 'error' ? 'alert' : type === 'warn' ? 'info' : 'checkcircle', 18)}<div><b>${esc(msg)}</b>${sub ? `<span>${esc(sub)}</span>` : ''}</div>`; w.appendChild(x); setTimeout(() => x.remove(), 3400); }
function modal(o) { const m = el('#modalRoot'); m.innerHTML = `<div class="modal ${o.size || ''}" role="dialog"><div class="modal-head"><h3>${o.title}</h3><button class="icon-btn" onclick="closeModal()">${ic('x')}</button></div><div class="modal-body" id="modalBody">${o.body}</div>${o.foot ? `<div class="modal-foot">${o.foot}</div>` : ''}</div>`; m.classList.add('show'); if (o.onOpen) setTimeout(o.onOpen, 0); }
function closeModal() { const m = el('#modalRoot'); m.classList.remove('show'); m.innerHTML = ''; App.editing = null; }
function confirmDlg(title, msg, onOk, danger) { window.__cb = onOk; modal({ title, size: 'sm', body: `<p>${msg}</p>`, foot: `<button class="btn" onclick="closeModal()">${t('cancel')}</button><button class="btn ${danger ? 'danger' : 'primary'}" onclick="closeModal();window.__cb&&window.__cb()">${t('confirm')}</button>` }); }
function tabs(id, items, active) { return `<div class="tabs" id="${id}">${items.map(([k, l]) => `<button class="tab ${k === active ? 'active' : ''}" data-tab="${k}" onclick="switchTab('${id}','${k}')">${lab(l)}</button>`).join('')}</div>`; }
function pane(id, k, html, active) { return `<div class="tabpane ${active ? '' : 'hidden'}" data-for="${id}" data-tab="${k}">${html}</div>`; }
function switchTab(id, k) { const root = el('#' + id); if (!root) return; (App.store.activeTab = App.store.activeTab || {})[id] = k; root.querySelectorAll('.tab').forEach(b => b.classList.toggle('active', b.dataset.tab === k)); els(`.tabpane[data-for="${id}"]`).forEach(p => p.classList.toggle('hidden', p.dataset.tab !== k)); }
function barcode(code) { let s = String(code), bars = ''; for (let i = 0; i < s.length; i++) { const c = s.charCodeAt(i); bars += `<i style="width:${1 + (c % 3)}px;margin-right:${1 + ((c >> 2) % 2)}px"></i><i style="width:${1 + ((c >> 1) % 2)}px;margin-right:1px"></i>`; } return `<div class="barcode">${bars}</div><div class="mono small">${esc(code)}</div>`; }
function pct(v) { return `<div class="pct"><i style="width:${Math.max(0, Math.min(100, v))}%"></i></div>`; }

/* ---------- forms ---------- */
function field(f, v) {
  const val = (v && v[f.k] != null) ? v[f.k] : (f.val != null ? f.val : '');
  const req = f.req ? ' <span class="req">*</span>' : '';
  const lbl = `<label>${lab(f.l)}${req}</label>`;
  const attrs = `data-key="${f.k}" ${f.readonly ? 'readonly' : ''} ${f.onchange ? `onchange="${f.onchange}"` : ''} ${f.oninput ? `oninput="${f.oninput}"` : ''} ${f.ph ? `placeholder="${esc(lab(f.ph))}"` : ''} ${f.attrs || ''}`;
  const opts = typeof f.opts === 'function' ? f.opts() : (f.opts || []);
  let c = '';
  switch (f.type || 'text') {
    case 'select': c = `<select class="inp ed" ${attrs}><option value="">${t('select')}</option>${opts.map(o => { const [ov, ol] = Array.isArray(o) ? o : [o, o]; return `<option value="${esc(ov)}" ${String(val) === String(ov) ? 'selected' : ''}>${esc(typeof ol === 'function' ? ol() : ol)}</option>`; }).join('')}</select>`; break;
    case 'textarea': c = `<textarea class="inp ed" ${attrs} rows="${f.rows || 3}">${esc(val)}</textarea>`; break;
    case 'toggle': { const on = f.k === 'status' ? val !== 'Inactive' : (val === true || val === 'Yes' || val === 'true'); c = `<div class="switch-row"><div class="switch ed-toggle ${on ? 'on' : ''}" data-key="${f.k}" onclick="toggleSwitch(this)"></div><span class="small muted">${on ? (f.onLabel ? lab(f.onLabel) : t('enabled')) : (f.offLabel ? lab(f.offLabel) : t('disabled'))}</span></div>`; break; }
    case 'check': c = `<label class="check" style="min-height:36px"><input type="checkbox" class="ed" data-key="${f.k}" ${val === true || val === 'Yes' ? 'checked' : ''}> ${lab(f.text || f.l)}</label>`; break;
    case 'chips': { const vals = Array.isArray(val) ? val : String(val || '').split(',').map(x => x.trim()).filter(Boolean); c = `<div class="chips ed-chips" data-key="${f.k}">${opts.map(o => { const [ov, ol] = Array.isArray(o) ? o : [o, o]; return `<label><input type="checkbox" value="${esc(ov)}" ${vals.includes(ov) ? 'checked' : ''}>${esc(typeof ol === 'function' ? ol() : ol)}</label>`; }).join('')}</div>`; break; }
    case 'static': c = `<div class="inp" style="background:var(--surface-2)">${f.html || esc(val)}</div>`; break;
    case 'html': c = f.html || ''; break;
    default: c = `<input class="inp ed" type="${f.type || 'text'}" value="${esc(val)}" ${attrs} ${f.min != null ? `min="${f.min}"` : ''} ${f.max != null ? `max="${f.max}"` : ''} ${f.step != null ? `step="${f.step}"` : ''}>`;
  }
  return `<div class="field ${f.span ? 'span' + f.span : ''}" data-req="${f.req ? 1 : 0}">${lbl}${c}${f.help ? `<div class="help">${lab(f.help)}</div>` : ''}<div class="val-msg">${lab(f.l)} ${t('required')}</div></div>`;
}
function formGrid(fields, values, cols = '') { return `<div class="form-grid ${cols}">${fields.map(f => field(f, values)).join('')}</div>`; }
function readForm(root) { root = root || el('#modalBody'); const o = {}; root.querySelectorAll('.ed').forEach(e => { if (e.type === 'checkbox') o[e.dataset.key] = e.checked; else if (e.type === 'number') o[e.dataset.key] = e.value === '' ? '' : Number(e.value); else o[e.dataset.key] = e.value; }); root.querySelectorAll('.ed-toggle').forEach(e => { const on = e.classList.contains('on'); o[e.dataset.key] = e.dataset.key === 'status' ? (on ? 'Active' : 'Inactive') : on; }); root.querySelectorAll('.ed-chips').forEach(e => { o[e.dataset.key] = [...e.querySelectorAll('input:checked')].map(i => i.value); }); return o; }
function validateForm(root) { root = root || el('#modalBody'); let ok = true; root.querySelectorAll('.field[data-req="1"]').forEach(w => { const e = w.querySelector('.ed'); if (!e || e.type === 'checkbox') return; const bad = !String(e.value).trim(); w.classList.toggle('invalid', bad); if (bad) ok = false; }); if (!ok) toast(t('fillMandatory'), 'error'); return ok; }
function toggleSwitch(x) { x.classList.toggle('on'); const s = x.nextElementSibling; if (s && s.tagName === 'SPAN') s.textContent = x.classList.contains('on') ? t('enabled') : t('disabled'); }

/* ---------- grid ---------- */
function grid(o) {
  const cols = o.cols, rows = o.rows || [];
  const head = cols.map(c => `<th style="${c.w ? 'width:' + c.w : ''}" class="${c.cls || ''}">${lab(c.l)}</th>`).join('') + (o.actions ? `<th class="actions">${t('actions')}</th>` : '');
  const body = rows.length ? rows.map((r, i) => `<tr class="${o.rowClass ? o.rowClass(r) : ''} ${o.onRow ? 'clickable' : ''}" ${o.onRow ? `onclick="${o.onRow.replace('%id', r.id)}"` : ''} id="${o.id ? o.id + '-' + r.id : ''}">${cols.map(c => `<td class="${c.cls || ''}">${c.f ? c.f(r, i) : esc(L(r, c.k))}</td>`).join('')}${o.actions ? `<td class="actions" onclick="event.stopPropagation()">${o.actions(r)}</td>` : ''}</tr>`).join('') : `<tr><td colspan="${cols.length + (o.actions ? 1 : 0)}"><div class="empty">${o.empty || t('noResults')}</div></td></tr>`;
  const foot = o.nofoot ? '' : `<div class="grid-foot"><span>${t('showing')} ${rows.length} ${t('of')} ${o.total != null ? o.total : rows.length} ${t('records')}</span><div class="pager"><button>‹</button><button class="active">1</button><button>›</button></div></div>`;
  return `<div class="table-wrap"><table class="g ${o.compact ? 'compact' : ''}"><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table></div>${foot}`;
}
const rowBtn = (icon, title, onclick, cls = '') => `<button class="row-btn ${cls}" title="${esc(lab(title))}" onclick="${onclick}">${ic(icon, 14)}</button>`;

/* ---------- generic CRUD screens ---------- */
function crudState(key) { return App.store[key] || (App.store[key] = { q: '', st: '', extra: {} }); }
function crudPage(cfg) {
  const st = crudState(cfg.key);
  const rows = crudRows(cfg);
  const extra = (cfg.filters || []).map(f => `<select class="inp sm" onchange="crudFilter('${cfg.key}','${f.k}',this.value)"><option value="">${lab(f.l)}: ${t('all')}</option>${(typeof f.opts === 'function' ? f.opts() : f.opts).map(o => { const [ov, ol] = Array.isArray(o) ? o : [o, o]; return `<option value="${esc(ov)}" ${st.extra[f.k] === ov ? 'selected' : ''}>${esc(typeof ol === 'function' ? ol() : ol)}</option>`; }).join('')}</select>`).join('');
  const actions = (cfg.canAdd === false ? '' : `<button class="btn primary" onclick="crudOpen('${cfg.key}')">${ic('plus')} ${t('add')} ${t(cfg.singular)}</button>`) + (cfg.headActions ? cfg.headActions() : '');
  return `${pageHead(t(cfg.title), cfg.crumb || `${t(cfg.group || 'setup')} / ${t(cfg.title)}`, actions)}${cfg.note ? `<div class="info">${lab(cfg.note)}</div>` : ''}${cfg.before ? cfg.before() : ''}
<div class="card"><div class="toolbar"><div class="search-wrap">${ic('search')}<input class="inp" placeholder="${t('searchPlaceholder')}" value="${esc(st.q)}" oninput="crudSearch('${cfg.key}',this.value)"></div>${cfg.noStatus ? '' : `<select class="inp sm" onchange="crudStatus('${cfg.key}',this.value)"><option value="">${t('allStatuses')}</option><option value="Active" ${st.st === 'Active' ? 'selected' : ''}>${t('st_Active')}</option><option value="Inactive" ${st.st === 'Inactive' ? 'selected' : ''}>${t('st_Inactive')}</option></select>`}${extra}<div class="grow"></div><button class="btn" onclick="toast(t('exported'),'ok')">${ic('download')} ${t('exportExcel')}</button></div><div id="crudGrid">${crudGrid(cfg, rows)}</div></div>${cfg.after ? cfg.after() : ''}`;
}
function crudRows(cfg) { const st = crudState(cfg.key); let rows = [...(cfg.rows ? cfg.rows() : DB[cfg.data])]; const q = st.q.toLowerCase(); if (q) rows = rows.filter(r => Object.values(r).some(v => typeof v !== 'object' && String(v).toLowerCase().includes(q))); if (st.st) rows = rows.filter(r => r.status === st.st); for (const [k, v] of Object.entries(st.extra)) if (v) rows = rows.filter(r => String(r[k]) === String(v)); return rows; }
function crudGrid(cfg, rows) {
  return grid({ cols: cfg.cols, rows, compact: cfg.compact, total: (cfg.rows ? cfg.rows() : DB[cfg.data]).length, rowClass: cfg.rowClass, actions: r => (cfg.rowActions ? cfg.rowActions(r) : '') + (cfg.canEdit === false ? '' : rowBtn('edit', 'edit', `crudOpen('${cfg.key}',${r.id})`)) + (cfg.canDelete === false ? '' : rowBtn('trash', 'delete', `crudDelete('${cfg.key}',${r.id})`, 'danger')) });
}
function crudRefresh(key) { const cfg = App.screens[key]; const g = el('#crudGrid'); if (g && cfg && cfg.data) g.innerHTML = crudGrid(cfg, crudRows(cfg)); }
function crudSearch(key, v) { crudState(key).q = v; crudRefresh(key); }
function crudStatus(key, v) { crudState(key).st = v; crudRefresh(key); }
function crudFilter(key, k, v) { crudState(key).extra[k] = v; crudRefresh(key); }
function crudOpen(key, id) {
  const cfg = App.screens[key]; const row = id ? DB[cfg.data].find(r => r.id === id) : {};
  App.editing = { key, id: id || null };
  if (!cfg.editor && !cfg.fields) return;
  const body = cfg.editor ? cfg.editor(row, !!id) : formGrid(typeof cfg.fields === 'function' ? cfg.fields(row) : cfg.fields, row, cfg.formCols || '');
  modal({ title: `${id ? t('edit') : t('add')} ${t(cfg.singular)}`, size: cfg.size || (cfg.wide ? 'lg' : ''), body, foot: `<button class="btn" onclick="closeModal()">${t('cancel')}</button><button class="btn primary" onclick="crudSave('${key}')">${ic('save')} ${id ? t('saveChanges') : t('add') + ' ' + t(cfg.singular)}</button>`, onOpen: cfg.onOpen ? () => cfg.onOpen(row, !!id) : null });
}
function crudSave(key) {
  const cfg = App.screens[key]; const root = el('#modalBody'); if (!validateForm(root)) return;
  let obj = cfg.read ? cfg.read(root) : readForm(root); if (obj === false) return;
  const arr = DB[cfg.data]; const id = App.editing.id;
  if (cfg.beforeSave) { const err = cfg.beforeSave(obj, id ? arr.find(r => r.id === id) : null); if (err) { toast(err, 'error'); return; } }
  if (id) { const i = arr.findIndex(r => r.id === id); arr[i] = { ...arr[i], ...obj }; } else { obj.id = Math.max(0, ...arr.map(r => r.id)) + 1; if (obj.status == null) obj.status = 'Active'; if (cfg.onCreate) cfg.onCreate(obj); arr.unshift(obj); }
  closeModal(); renderMain(); toast(`${t(cfg.singular)} ${id ? t('updated') : t('added')}`, 'ok');
}
function crudDelete(key, id) {
  const cfg = App.screens[key]; const row = DB[cfg.data].find(r => r.id === id);
  if (cfg.refCheck) { const msg = cfg.refCheck(row); if (msg) { toast(t('deleteBlocked'), 'error', msg); return; } }
  confirmDlg(`${t('delete')} ${t(cfg.singular)}`, `${t('confirmDelete')} <b>${esc(L(row) || row.code || row.id)}</b>? ${t('deleteHint')}`, () => { DB[cfg.data] = DB[cfg.data].filter(r => r.id !== id); renderMain(); toast(t('deleted'), 'ok'); }, true);
}

/* ---------- shell & navigation ---------- */
function renderShell() {
  el('#app').innerHTML = `<div class="shell ${App.collapsed ? 'collapsed' : ''}" id="shell"><aside class="sidebar" id="sidebar">${sidebarHtml()}</aside><div class="scrim" id="scrim" onclick="closeDrawer()"></div><div class="content"><header class="topbar">${topbarHtml()}</header><main class="main" id="main"></main><footer class="foot"><span>© 2026 National Technology · Sky LIS</span><span>${App.portal === 'admin' ? t('superAdminPortal') : t('clientPortal')} · ${t('prototype')} ${App.version}</span></footer></div></div><div class="menu-pop hidden" id="profileMenu"></div>`;
  renderMain();
}
function sidebarHtml() {
  const q = (App.store.navq || '').toLowerCase();
  return `<div class="sb-brand"><div class="logo-card"><img src="${LOGO}" alt="Sky LIS"></div></div>${App.tenant ? `<div class="sb-tenant"><b>${esc(L(App.tenant))}</b>${t('clientPortal')}</div>` : `<div class="sb-tenant"><b>National Technology</b>${t('superAdminPortal')}</div>`}
<div class="sb-search">${ic('search', 14)}<input placeholder="${t('filterMenu')}" value="${esc(App.store.navq || '')}" oninput="App.store.navq=this.value;el('#sidebar').innerHTML=sidebarHtml();el('#sidebar .sb-search input').focus()"></div>
<div class="sb-scroll">${App.nav.map(g => { const items = g.items.filter(i => !q || t(i.l).toLowerCase().includes(q)); if (!items.length) return ''; return `<div class="nav-group">${t(g.g)}</div>${items.map(i => `<div class="nav-item ${App.screen === i.k ? 'active' : ''}" onclick="navigate('${i.k}')" title="${esc(t(i.l))}">${ic(i.i || 'dot', 16)}<span class="txt">${t(i.l)}</span>${i.cnt ? `<span class="cnt">${i.cnt()}</span>` : ''}</div>`).join('')}`; }).join('')}</div>`;
}
function topbarHtml() {
  return `<button class="icon-btn mobile-only" onclick="openDrawer()">${ic('menu')}</button><button class="icon-btn desktop-only" onclick="toggleCollapse()" title="${t('collapseNav')}">${ic('menu')}</button><div><h2 id="topTitle">${screenTitle()}</h2></div><div class="grow"></div>
${App.branch ? `<span class="pill brand">${ic('pin', 13)} ${esc(L(App.branch))}</span>` : ''}${App.wallet != null ? `<span class="pill">${ic('wallet', 13)} ${t('walletBalance')}: ${money(App.wallet)}</span>` : ''}
<button class="lang-btn" onclick="setLang('${App.lang === 'en' ? 'ar' : 'en'}')">${ic('globe', 13)} ${App.lang === 'en' ? 'العربية' : 'English'}</button>
<button class="icon-btn" title="${t('notifications')}" onclick="showNotifications()">${ic('bell')}</button>
<div class="profile" onclick="toggleProfile(event)"><div class="avatar">${esc((App.user?.initials) || 'U')}</div><div class="who desktop-only"><b>${esc(L(App.user, 'name'))}</b><span>${esc(L(App.user, 'role'))}</span></div>${ic('chevdown', 14)}</div>`;
}
function screenTitle() { for (const g of App.nav) for (const i of g.items) if (i.k === App.screen) return t(i.l); const s = App.screens[App.screen]; return s && s.title ? t(s.title) : t('home'); }
function navigate(k, params) { if (!App.screens[k]) { toast(t('screenNotBuilt'), 'warn', k); return; } App.screen = k; App.params = params || {}; closeDrawer(); el('#sidebar').innerHTML = sidebarHtml(); const tt = el('#topTitle'); if (tt) tt.textContent = screenTitle(); renderMain(); window.scrollTo(0, 0); }
function renderMain() { const s = App.screens[App.screen]; const m = el('#main'); if (!m) return; try { m.innerHTML = typeof s === 'function' ? s(App.params) : crudPage(s); } catch (e) { console.error(e); m.innerHTML = `<div class="bad-strip">${esc(e.message)}</div>`; } }
function toggleCollapse() { App.collapsed = !App.collapsed; el('#shell').classList.toggle('collapsed', App.collapsed); }
function openDrawer() { el('#sidebar').classList.add('open'); el('#scrim').classList.add('show'); }
function closeDrawer() { const s = el('#sidebar'); if (s) s.classList.remove('open'); const sc = el('#scrim'); if (sc) sc.classList.remove('show'); }
function toggleProfile(e) { e.stopPropagation(); const m = el('#profileMenu'); m.innerHTML = `<div class="mi" onclick="navigate('profile');hideProfile()">${ic('user')} ${t('profile')}</div><div class="mi" onclick="hideProfile();toast(t('settings'),'ok')">${ic('settings')} ${t('settings')}</div><div class="mi" onclick="hideProfile();showShortcuts()">${ic('key')} ${t('keyboardShortcuts')}</div><div class="mi danger" onclick="logout()">${ic('logout')} ${t('logout')}</div>`; m.classList.toggle('hidden'); }
function hideProfile() { const m = el('#profileMenu'); if (m) m.classList.add('hidden'); }
document.addEventListener('click', hideProfile);
function setLang(l) { App.lang = l; document.documentElement.lang = l; document.documentElement.dir = l === 'ar' ? 'rtl' : 'ltr'; if (App.booted && !el('#app').classList.contains('hidden')) renderShell(); else if (typeof renderAuth === 'function') renderAuth(App.authMode || 'login'); }
function showNotifications() { modal({ title: t('notifications'), size: 'sm', body: `<ul class="timeline">${(DB.notifications || []).map(n => `<li><time>${esc(n.time)}</time><div><b>${esc(L(n, 'title'))}</b><div class="small muted">${esc(L(n, 'text'))}</div></div></li>`).join('') || `<li>${t('noResults')}</li>`}</ul>`, foot: `<button class="btn" onclick="closeModal()">${t('close')}</button>` }); }
function showShortcuts() { modal({ title: t('keyboardShortcuts'), size: 'sm', body: `<table class="g compact"><tbody>${[['Ctrl + S', t('ks_save')], ['Ctrl + K', t('ks_search')], ['Alt + N', t('ks_new')], ['F2', t('ks_lookup')], ['Esc', t('ks_close')]].map(([k, v]) => `<tr><td><span class="tag mono">${k}</span></td><td>${esc(v)}</td></tr>`).join('')}</tbody></table>`, foot: `<button class="btn" onclick="closeModal()">${t('close')}</button>` }); }
document.addEventListener('keydown', e => { if (e.key === 'Escape') closeModal(); if (e.ctrlKey && e.key.toLowerCase() === 's') { const b = el('#modalRoot.show .modal-foot .btn.primary'); if (b) { e.preventDefault(); b.click(); } } if (e.ctrlKey && e.key.toLowerCase() === 'k') { const i = el('#main .search-wrap input'); if (i) { e.preventDefault(); i.focus(); } } });
document.addEventListener('click', e => { if (e.target.id === 'modalRoot') closeModal(); });
function logout() { hideProfile(); App.booted = false; el('#app').classList.add('hidden'); if (typeof renderAuth === 'function') renderAuth('login'); }
const LOGO = window.LOGO_DATA || '';
