#!/usr/bin/env node
// Sky LIS — backend cycle validation (tester D, 2026-10-11).
//
// Drives the real API the way the SPA does (credential envelope, X-Requested-With, cookie endpoints, branch selection)
// and, after every write, reads PostgreSQL as the owner role to assert what was stored: values, tenant_id, foreign keys,
// audit rows (audit.field_changes / audit.audit_trail, matched by the X-Correlation-Id this script sends) and the outbox
// events of the same transaction (ops.outbox_events, polled until the outbox worker has processed them).
//
// Usage (Node 24, no dependencies; run from the repository root):
//   node tests/validation/backend-cycle.mjs [baseUrl]                    full cycle, default http://127.0.0.1:5102
//   node tests/validation/backend-cycle.mjs [baseUrl] --smoke              envelope + sign-in + sign-out only
//   node tests/validation/backend-cycle.mjs https://192.168.80.38:5443 --deployed --insecure-tls
//                                                                        short read-mostly run against the installed site
//                                                                        (reuses the client created by the last full run)
// Options: --json <file> writes the step records as JSON (default .local/validation/backend-cycle-<run>.json).
//
// Database access: psql at 127.0.0.1:5433, db `lis`; passwords from deploy/postgres/.env (LIS_OWNER_PASSWORD,
// LIS_APP_PASSWORD), passed only through PGPASSWORD of the child process. The only statement run as lis_app is the
// read-only row-level-security probe (BEGIN READ ONLY … ROLLBACK). Nothing is written with psql.
//
// The script creates its own client ("V11 API Lab", a run suffix when it already exists) and records the generated
// passwords in .local/dev-credentials.md and .local/validation/backend-cycle.state.json (both git-ignored).
// Exit code: 0 when every step passed, 1 otherwise.

import crypto from 'node:crypto';
import fs from 'node:fs';
import http from 'node:http';
import https from 'node:https';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

// ---- configuration --------------------------------------------------------------------------------------------------

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const args = process.argv.slice(2);
const flag = (name) => args.includes(name);
const option = (name) => {
  const index = args.indexOf(name);
  return index >= 0 ? args[index + 1] : undefined;
};
const BASE = (args.find((value) => /^https?:\/\//.test(value)) ?? 'http://127.0.0.1:5102').replace(/\/+$/, '');
const DEPLOYED = flag('--deployed');
const INSECURE_TLS = flag('--insecure-tls');
const SMOKE = flag('--smoke');
const RUN = new Date().toISOString().replace(/[-:TZ.]/g, '').slice(2, 14); // yymmddHHMMSS
const STATE_FILE = path.join(ROOT, '.local', 'validation', 'backend-cycle.state.json');
const JSON_OUT = option('--json') ?? path.join(ROOT, '.local', 'validation', `backend-cycle-${DEPLOYED ? 'deployed-' : ''}${RUN}.json`);
const CREDENTIALS_FILE = path.join(ROOT, '.local', 'dev-credentials.md');
const PSQL = 'C:\\Program Files\\PostgreSQL\\17\\bin\\psql.exe';
const PLATFORM_TENANT = '00000000-0000-0000-0000-000000000001';
const DEMO_TENANT = '01a12494-7ca8-75a8-873d-645d12599db7';
const DEMO_MAIN_BRANCH = '01a12494-8a11-70e5-a5b7-58214118cca7';
const OUTBOX_WAIT_MS = 20_000;

// ---- small utilities ------------------------------------------------------------------------------------------------

const b64u = (buffer) => Buffer.from(buffer).toString('base64url');
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const lit = (value) => `'${String(value).replace(/'/g, "''")}'`;
const short = (value, max = 160) => {
  const text = typeof value === 'string' ? value : JSON.stringify(value);
  return text === undefined ? '' : text.length > max ? `${text.slice(0, max)}…` : text;
};
const randomDigits = (count) => Array.from(crypto.randomBytes(count), (byte) => String(byte % 10)).join('');
const newPassword = (prefix) => `${prefix}-${b64u(crypto.randomBytes(9))}-9k`;
const num = (value) => (value === null || value === undefined ? value : Number(value));

function readEnvFile() {
  const text = fs.readFileSync(path.join(ROOT, 'deploy', 'postgres', '.env'), 'utf8');
  const result = {};
  for (const line of text.split(/\r?\n/)) {
    const match = /^([A-Z0-9_]+)=(.*)$/.exec(line.trim());
    if (match) result[match[1]] = match[2];
  }
  return result;
}
const ENV = readEnvFile();

// ---- database (psql, owner role; read-only use) ---------------------------------------------------------------------

function psql(role, statements) {
  const password = role === 'app' ? ENV.LIS_APP_PASSWORD : ENV.LIS_OWNER_PASSWORD;
  const user = role === 'app' ? 'lis_app' : 'lis_owner';
  const argv = ['-h', '127.0.0.1', '-p', '5433', '-U', user, '-d', 'lis', '-X', '-q', '-At', '-v', 'ON_ERROR_STOP=1'];
  for (const statement of statements) argv.push('-c', statement);
  return execFileSync(PSQL, argv, { env: { ...process.env, PGPASSWORD: password, PGCLIENTENCODING: 'UTF8' }, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
}

/** Rows of a query as JSON objects (owner role, bypasses RLS). */
function rows(query) {
  const out = psql('owner', [`select coalesce(json_agg(_r), '[]'::json) from (${query}) _r`]).trim();
  return JSON.parse(out || '[]');
}
const one = (query) => rows(query)[0] ?? null;
const scalar = (query) => {
  const row = one(query);
  return row ? Object.values(row)[0] : null;
};
const dbNow = () => scalar('select clock_timestamp() as now');

async function poll(read, done, timeoutMs = OUTBOX_WAIT_MS, intervalMs = 500) {
  const started = Date.now();
  let value = read();
  while (!done(value) && Date.now() - started < timeoutMs) {
    await sleep(intervalMs);
    value = read();
  }
  return { value, ok: done(value), ms: Date.now() - started };
}

// ---- HTTP -----------------------------------------------------------------------------------------------------------

class Api {
  constructor(base, { insecureTls = false } = {}) {
    this.base = base;
    this.url = new URL(base);
    this.origin = this.url.origin;
    this.isHttps = this.url.protocol === 'https:';
    // Certificate verification is disabled only for this agent and only when --insecure-tls is given (self-signed site).
    this.agent = this.isHttps ? new https.Agent({ keepAlive: true, rejectUnauthorized: !insecureTls }) : new http.Agent({ keepAlive: true });
    this.counter = 0;
  }

  pathOf(route) {
    return new URL(this.base + route).pathname;
  }

  nextCorrelation() {
    this.counter += 1;
    return `v11-${RUN}-${String(this.counter).padStart(3, '0')}`;
  }

  request(method, route, { body, headers = {}, token, cookie, raw } = {}) {
    const target = new URL(this.base + route);
    const correlation = headers['X-Correlation-Id'] ?? this.nextCorrelation();
    const payload = body === undefined ? undefined : raw ? body : Buffer.from(JSON.stringify(body), 'utf8');
    const all = {
      Accept: 'application/json',
      'X-Requested-With': 'Platform',
      'X-Correlation-Id': correlation,
      'User-Agent': 'lis-validation-backend-cycle/1.0',
      ...headers,
    };
    if (payload !== undefined) {
      all['Content-Type'] = all['Content-Type'] ?? 'application/json';
      all['Content-Length'] = payload.length;
    }
    if (token) all.Authorization = `Bearer ${token}`;
    if (cookie) all.Cookie = cookie;
    const transport = this.isHttps ? https : http;
    return new Promise((resolve, reject) => {
      const request = transport.request(target, { method, headers: all, agent: this.agent, timeout: 60_000 }, (response) => {
        const chunks = [];
        response.on('data', (chunk) => chunks.push(chunk));
        response.on('end', () => {
          const text = Buffer.concat(chunks).toString('utf8');
          let json = null;
          if (/json/.test(response.headers['content-type'] ?? '') && text.length > 0) {
            try {
              json = JSON.parse(text);
            } catch {
              json = null;
            }
          }
          resolve({ status: response.statusCode, headers: response.headers, text, json, correlation: response.headers['x-correlation-id'] ?? correlation, method, route });
        });
      });
      request.on('timeout', () => request.destroy(new Error(`timeout ${method} ${route}`)));
      request.on('error', reject);
      if (payload !== undefined) request.write(payload);
      request.end();
    });
  }
}

/** Seals one credential exactly as frontend/src/app/core/crypto/credential-envelope.ts does (compact JWE, RSA-OAEP-256 + A256GCM). */
function seal(challenge, field, value, audience) {
  const header = b64u(Buffer.from(JSON.stringify({ alg: 'RSA-OAEP-256', enc: 'A256GCM', kid: challenge.kid }), 'utf8'));
  const publicKey = crypto.createPublicKey({ key: { kty: 'RSA', n: challenge.publicKey.n, e: challenge.publicKey.e }, format: 'jwk' });
  const cek = crypto.randomBytes(32);
  const wrapped = crypto.publicEncrypt({ key: publicKey, padding: crypto.constants.RSA_PKCS1_OAEP_PADDING, oaepHash: 'sha256' }, cek);
  const iv = crypto.randomBytes(12);
  const cipher = crypto.createCipheriv('aes-256-gcm', cek, iv, { authTagLength: 16 });
  cipher.setAAD(Buffer.from(header, 'ascii'));
  const ciphertext = Buffer.concat([cipher.update(JSON.stringify({ v: value, f: field, aud: audience, n: challenge.nonce }), 'utf8'), cipher.final()]);
  return [header, b64u(wrapped), b64u(iv), b64u(ciphertext), b64u(cipher.getAuthTag())].join('.');
}

class Session {
  constructor(api, label) {
    this.api = api;
    this.label = label;
    this.token = null;
    this.cookie = null; // "name=value" of the refresh cookie
    this.profile = null;
  }

  call(method, route, body, headers) {
    return this.api.request(method, route, { body, headers, token: this.token });
  }

  get(route, headers) {
    return this.call('GET', route, undefined, headers);
  }

  post(route, body, headers) {
    return this.call('POST', route, body, headers);
  }

  put(route, body, headers) {
    return this.call('PUT', route, body, headers);
  }

  async challenge() {
    const response = await this.api.request('GET', '/api/v1/auth/credential-challenge');
    if (response.status !== 200) throw new Error(`credential-challenge ${response.status} ${short(response.text)}`);
    return response.json;
  }

  /** POST with the listed [CredentialField] properties sealed under one fresh challenge (audience = METHOD + path). */
  async postSealed(route, body, fields, headers) {
    const challenge = await this.challenge();
    const audience = `POST ${this.api.pathOf(route)}`;
    const sealed = { ...body };
    for (const field of fields) sealed[field] = seal(challenge, field, body[field], audience);
    return this.post(route, sealed, headers);
  }

  captureCookie(response) {
    const setCookie = [].concat(response.headers['set-cookie'] ?? []);
    const line = setCookie.find((value) => /^(__Secure-rt|rt)=/.test(value));
    if (line) this.cookie = line.split(';')[0];
    return line ?? null;
  }

  async signIn(username, password) {
    const response = await this.postSealed('/api/v1/auth/sign-in', { tenant: null, username, password }, ['password']);
    this.lastSetCookie = response.status === 200 ? this.captureCookie(response) : null;
    if (response.status === 200 && response.json?.step === 'signed-in') {
      this.token = response.json.accessToken;
      this.profile = response.json.profile;
    }
    return response;
  }

  /** The two cookie endpoints (refresh, sign-out) need Origin + Sec-Fetch-Site + X-Requested-With (AuthOriginCheckMiddleware). */
  cookiePost(route) {
    return this.api.request('POST', route, { cookie: this.cookie, headers: { Origin: this.api.origin, 'Sec-Fetch-Site': 'same-origin' } });
  }
}

// ---- step recording -------------------------------------------------------------------------------------------------

const steps = [];
const ctx = {};

class Step {
  constructor(id, title) {
    this.id = id;
    this.title = title;
    this.request = '';
    this.expected = '';
    this.observed = [];
    this.db = [];
    this.failures = [];
    this.verdict = 'PASS';
  }

  req(text) {
    this.request = this.request ? `${this.request}; ${text}` : text;
  }

  expect(text) {
    this.expected = this.expected ? `${this.expected}; ${text}` : text;
  }

  saw(text) {
    this.observed.push(text);
  }

  /** An API-level assertion. */
  check(condition, label, detail) {
    if (!condition) {
      this.failures.push(`${label}${detail === undefined ? '' : ` (got ${short(detail)})`}`);
    }
    return condition;
  }

  /** A database assertion (reported in the DB column). */
  dbCheck(condition, label, detail) {
    this.db.push(`${condition ? 'ok' : 'FAIL'} ${label}${!condition && detail !== undefined ? ` (got ${short(detail)})` : ''}`);
    if (!condition) this.failures.push(`DB: ${label}${detail === undefined ? '' : ` (got ${short(detail)})`}`);
    return condition;
  }

  /** An HTTP status assertion that records what came back. */
  status(response, expected, label = '') {
    const list = [].concat(expected);
    const code = response.json?.code;
    this.saw(`${label ? `${label} ` : ''}${response.status}${code ? ` ${code}` : ''}`);
    return this.check(list.includes(response.status), `${label || response.route} HTTP ${list.join('/')}`, `${response.status} ${short(response.text, 300)}`);
  }

  code(response, expected, label = '') {
    return this.check(response.json?.code === expected, `${label}code ${expected}`, response.json?.code ?? response.text);
  }
}

async function step(id, title, body) {
  const record = new Step(id, title);
  const started = Date.now();
  try {
    await body(record);
  } catch (error) {
    record.failures.push(`exception: ${error.message}`);
  }
  record.ms = Date.now() - started;
  if (record.failures.length > 0) record.verdict = 'FAIL';
  steps.push(record);
  const mark = record.verdict === 'PASS' ? 'PASS' : 'FAIL';
  console.log(`${mark}  ${id.padEnd(6)} ${title}${record.failures.length ? `\n        ${record.failures.join('\n        ')}` : ''}`);
  return record;
}

const need = (value, what) => {
  if (value === undefined || value === null || value === '') throw new Error(`prerequisite missing: ${what}`);
  return value;
};

// ---- audit + outbox verification -------------------------------------------------------------------------------------

/**
 * Asserts the audit rows written by the request with correlation id `corr` and that the outbox events of the same
 * transaction (ops.outbox_events.id = audit.audit_trail.event_id) were processed by the worker.
 *   fieldChanges: ['Patient:Created', …]  (entity_type:action, each must be present)
 *   events:       ['PatientRegistered', …] (last segment of the event type, each must be present)
 */
async function verifyAuditAndOutbox(s, corr, tenant, { fieldChanges = [], events = [], reason, actor } = {}) {
  const fc = rows(`select entity_type, action, tenant_id, reason, actor_user_id from audit.field_changes where correlation_id = ${lit(corr)}`);
  const trail = rows(`select event_id, event_type, tenant_id, aggregate_type, aggregate_id, sequence from audit.audit_trail where correlation_id = ${lit(corr)} order by sequence`);
  const fcKeys = [...new Set(fc.map((row) => `${row.entity_type}:${row.action}`))];
  const eventKeys = trail.map((row) => row.event_type.split('.').pop());
  for (const expected of fieldChanges) s.dbCheck(fcKeys.includes(expected), `field_changes ${expected}`, fcKeys);
  for (const expected of events) s.dbCheck(eventKeys.includes(expected), `audit_trail ${expected}`, eventKeys);
  if (fc.length > 0) s.dbCheck(fc.every((row) => row.tenant_id === tenant), `field_changes tenant_id = ${tenant.slice(0, 8)}…`, [...new Set(fc.map((row) => row.tenant_id))]);
  // Since validation fix C-17 a platform-trail event (TenantProvisioned) is filed twice with the same event id: in the
  // client's trail and in the platform trail. Every other row belongs to the acting tenant.
  const PLATFORM_TENANT = '00000000-0000-0000-0000-000000000001';
  const platformCopies = trail.filter((row) => row.tenant_id === PLATFORM_TENANT && tenant !== PLATFORM_TENANT);
  if (trail.length > 0) s.dbCheck(trail.every((row) => row.tenant_id === tenant || (row.tenant_id === PLATFORM_TENANT && row.event_type.endsWith('TenantProvisioned'))), 'audit_trail tenant_id (platform copy only for TenantProvisioned)', [...new Set(trail.map((row) => row.tenant_id))]);
  for (const copy of platformCopies) s.dbCheck(trail.some((row) => row.event_id === copy.event_id && row.tenant_id === tenant), `platform trail copy of ${copy.event_type.split('.').pop()} shares its event id with the client's row`);
  if (actor !== undefined && fc.length > 0) s.dbCheck(fc.every((row) => row.actor_user_id === actor), 'field_changes actor_user_id = acting user', [...new Set(fc.map((row) => row.actor_user_id))]);
  if (actor !== undefined && trail.length > 0) s.dbCheck(rows(`select actor_user_id from audit.audit_trail where correlation_id = ${lit(corr)}`).every((row) => row.actor_user_id === actor), 'audit_trail actor_user_id = acting user');
  if (reason !== undefined) s.dbCheck(fc.some((row) => row.reason === reason), `field_changes reason "${reason}"`, fc.map((row) => row.reason));
  s.db.push(`audit: ${fc.length} field change(s) [${fcKeys.join(', ')}], ${trail.length} trail event(s) [${eventKeys.join(', ')}]`);

  if (trail.length === 0) return;
  const eventIds = [...new Set(trail.map((row) => row.event_id))];
  const ids = eventIds.map((id) => lit(id)).join(',');
  const result = await poll(
    () => rows(`select id, type, tenant_id, processed_at_utc, dead_lettered_at_utc, attempts, last_error from ops.outbox_events where id in (${ids})`),
    (list) => list.length === eventIds.length && list.every((row) => row.processed_at_utc !== null || row.dead_lettered_at_utc !== null),
  );
  const list = result.value;
  s.dbCheck(list.length === eventIds.length, `outbox holds the ${eventIds.length} event(s) of the transaction`, list.length);
  // Events of aggregates that are not ITenantScoped (saas: Tenant, Wallet) are enqueued with tenant_id NULL by design
  // (OutboxInterceptor) while AuditTrailInterceptor files them under the ambient tenant; every other event carries the tenant.
  const platformLevel = (row) => row.tenant_id === null && row.type.startsWith('Platform.Domain.PlatformAdmin.');
  s.dbCheck(list.every((row) => row.tenant_id === tenant || platformLevel(row)), 'outbox tenant_id (NULL only for platform-level events)', list.map((row) => `${row.type.split('.').pop()}:${row.tenant_id}`));
  const nulls = list.filter(platformLevel).map((row) => row.type.split('.').pop());
  if (nulls.length > 0) s.db.push(`note: outbox tenant_id NULL for platform-level ${nulls.join(', ')} (audit_trail files it under the tenant)`);
  const processed = list.filter((row) => row.processed_at_utc !== null).length;
  const dead = list.filter((row) => row.dead_lettered_at_utc !== null);
  s.dbCheck(processed === eventIds.length && dead.length === 0, `outbox processed ${processed}/${eventIds.length} within ${(result.ms / 1000).toFixed(1)} s`, dead.map((row) => `${row.type}: ${row.last_error}`));
}

/**
 * No credential material in what the transaction wrote to the audit tables or the outbox: neither the plaintext
 * password nor an Argon2id hash nor a field named like a hash or token (the audit interceptors must skip them).
 */
function verifyNoSecrets(s, corr, plaintexts) {
  const texts = rows(`select changes::text as t from audit.field_changes where correlation_id = ${lit(corr)}
    union all select payload::text from audit.audit_trail where correlation_id = ${lit(corr)}
    union all select o.payload::text from ops.outbox_events o join audit.audit_trail a on a.event_id = o.id where a.correlation_id = ${lit(corr)}`).map((row) => row.t ?? '');
  const hits = texts.filter((text) => /$argon2|passwordhash|securitystamp|refreshtokenhash|"password"/i.test(text) || plaintexts.some((secret) => secret && text.includes(secret)));
  s.dbCheck(texts.length > 0 && hits.length === 0, `no password, hash or security stamp in ${texts.length} audit/outbox payload(s)`, hits.map((text) => text.slice(0, 120)));
}

// ---- credentials and state -----------------------------------------------------------------------------------------

function platformCredentials() {
  const text = fs.readFileSync(CREDENTIALS_FILE, 'utf8');
  const find = (username) => {
    const match = new RegExp(`\\|\\s*${username.replace('.', '\\.')}\\s*\\|\\s*([^|\\s]+)\\s*\\|`).exec(text);
    return need(match?.[1], `password of ${username} in .local/dev-credentials.md`);
  };
  return { platform: find('platform.admin'), lab: find('lab.admin') };
}

function recordCredentials(lines) {
  const block = [
    '',
    `Backend-cycle validation run ${RUN} (tester D, \`tests/validation/backend-cycle.mjs\`, ${BASE}):`,
    '',
    '| Username | Password | Role | Tenant |',
    '| -------- | -------- | ---- | ------ |',
    ...lines.map((line) => `| ${line.username} | ${line.password} | ${line.role} | ${line.tenant} |`),
    '',
  ].join('\n');
  fs.appendFileSync(CREDENTIALS_FILE, block, 'utf8');
}

function saveState(state) {
  fs.mkdirSync(path.dirname(STATE_FILE), { recursive: true });
  fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2), 'utf8');
}

// ---- the full cycle -------------------------------------------------------------------------------------------------

async function fullCycle(api) {
  const creds = platformCredentials();
  const firstRun = scalar(`select count(*) as n from saas.tenants where name_en = 'V11 API Lab'`) === 0 && scalar(`select count(*) as n from identity.platform_users where username = 'v11api.admin'`) === 0;
  ctx.clientName = firstRun ? 'V11 API Lab' : `V11 API Lab ${RUN}`;
  ctx.slug = firstRun ? 'v11-api-lab' : `v11-api-lab-${RUN}`;
  ctx.adminUsername = firstRun ? 'v11api.admin' : `v11api.admin.${RUN}`;
  ctx.techUsername = firstRun ? 'v11api.tech' : `v11api.tech.${RUN}`;
  ctx.adminPassword = newPassword('V11adm');
  ctx.techPassword = newPassword('V11tec');
  ctx.managerMobile = `011${randomDigits(8)}`;
  const platform = new Session(api, 'platform.admin');
  const admin = new Session(api, ctx.adminUsername);
  const tech = new Session(api, ctx.techUsername);
  const lab = new Session(api, 'lab.admin');

  // ================================================================== 1. platform administrator provisions the client
  await step('P-01', 'platform.admin signs in (sealed password, refresh cookie)', async (s) => {
    s.req('GET credential-challenge; POST /api/v1/auth/sign-in {password: JWE}');
    s.expect('200 step=signed-in, scope platform, refresh cookie set');
    const response = await platform.signIn('platform.admin', creds.platform);
    s.status(response, 200);
    s.check(response.json?.step === 'signed-in', 'step signed-in', response.json?.step);
    s.check(response.json?.profile?.tenantId === PLATFORM_TENANT, 'platform tenant', response.json?.profile?.tenantId);
    s.check(platform.cookie !== null, 'refresh cookie issued', platform.lastSetCookie);
    s.saw(`cookie ${platform.lastSetCookie?.split('=')[0]} (${/HttpOnly/i.test(platform.lastSetCookie ?? '') ? 'HttpOnly' : 'no HttpOnly'}, ${/SameSite=Strict/i.test(platform.lastSetCookie ?? '') ? 'SameSite=Strict' : 'SameSite?'})`);
    const user = one(`select id from identity.platform_users where username = 'platform.admin'`);
    const session = one(`select id, tenant_id, is_revoked, rotated_at_utc from identity.refresh_sessions where platform_user_id = ${lit(user.id)} order by created_at_utc desc limit 1`);
    s.dbCheck(session?.tenant_id === PLATFORM_TENANT && session?.is_revoked === false, 'refresh_sessions row (platform tenant, live)', session);
  });

  await step('P-02', `Provision client "${ctx.clientName}" (plan STANDARD, Egypt/Cairo, admin ${ctx.adminUsername})`, async (s) => {
    const egypt = need(one(`select id from saas.countries where iso_code = 'EG'`), 'country Egypt').id;
    const cairo = need(one(`select id from saas.governorates where country_id = ${lit(egypt)} and name_en = 'Cairo'`), 'Cairo').id;
    const plan = need(one(`select id, code from saas.plans where code = 'STANDARD'`), 'plan STANDARD').id;
    const body = {
      nameEn: ctx.clientName, nameAr: 'معمل في11 للاختبار', countryId: egypt, governorateId: cairo, address: '11 Validation St., Nasr City',
      mobile: '01012345678', licenceNo: 'V11-LIC', managerName: 'V11 Manager', managerMobile: ctx.managerMobile, email: 'v11.api@lab.example',
      planId: plan, portalSlug: ctx.slug, language: 'en', adminUsername: ctx.adminUsername, adminPassword: ctx.adminPassword,
    };
    s.req('POST /api/v1/platform/clients {adminPassword: JWE}');
    s.expect('201 {id, portalUrl, administratorUsername}; tenant Active/Manual, wallet EGP 0, MAIN branch, admin user + primary membership + role, master-data copy, audit + outbox');
    const response = await platform.postSealed('/api/v1/platform/clients', body, ['adminPassword']);
    s.status(response, 201);
    const tenantId = need(response.json?.id, 'tenant id');
    ctx.tenant = tenantId;
    s.check(response.json?.administratorUsername === ctx.adminUsername, 'administratorUsername echoed', response.json?.administratorUsername);
    s.saw(`tenant ${tenantId} ${response.json?.portalUrl}`);
    recordCredentials([
      { username: ctx.adminUsername, password: ctx.adminPassword, role: 'Laboratory Administrator (provisioned by the script)', tenant: `${ctx.clientName} (${tenantId})` },
    ]);

    const tenant = one(`select * from saas.tenants where id = ${lit(tenantId)}`);
    s.dbCheck(tenant !== null, 'saas.tenants row');
    s.dbCheck(tenant?.name_en === ctx.clientName && tenant?.status === 'Active' && tenant?.source === 'Manual', 'tenant name/status Active/source Manual', tenant && [tenant.name_en, tenant.status, tenant.source]);
    s.dbCheck(tenant?.country_id === egypt && tenant?.governorate_id === cairo && tenant?.plan_id === plan, 'tenant country/governorate/plan FKs');
    s.dbCheck(tenant?.portal_slug === ctx.slug && tenant?.administrator_username === ctx.adminUsername, 'slug + administrator_username', tenant && [tenant.portal_slug, tenant.administrator_username]);
    s.dbCheck(tenant?.manager_mobile === `+20${ctx.managerMobile.slice(1)}`, 'manager mobile stored in E.164', tenant?.manager_mobile);
    const wallet = one(`select * from saas.wallets where tenant_id = ${lit(tenantId)}`);
    s.dbCheck(wallet !== null && num(wallet.balance) === 0 && wallet.currency_code === 'EGP', 'wallet (EGP, balance 0)', wallet && [wallet.balance, wallet.currency_code]);
    const branches = rows(`select id, code, currency, is_active, name_en from app.branches where tenant_id = ${lit(tenantId)}`);
    s.dbCheck(branches.length === 1 && branches[0].code === 'MAIN' && branches[0].is_active && branches[0].name_en === ctx.clientName, 'one MAIN branch named after the client', branches);
    ctx.mainBranch = branches[0]?.id;
    const user = one(`select id, username, is_active, mobile, must_change_password, left(password_hash, 10) as hash_prefix from identity.platform_users where username = ${lit(ctx.adminUsername)}`);
    s.dbCheck(user !== null && user.is_active && user.id === tenant?.administrator_user_id, 'administrator platform_users row = tenant.administrator_user_id', user);
    s.dbCheck(/^\$argon2id/.test(user?.hash_prefix ?? ''), 'password stored as an Argon2id hash', user?.hash_prefix);
    ctx.adminUserId = user?.id;
    const membership = one(`select m.*, r.name_en as role_name, r.tenant_id as role_tenant, r.special_can_pay, r.special_can_refund, r.special_can_cancel, r.special_can_modify_verified_result, r.special_discount_cap_percent, r.permission_keys from app.tenant_memberships m join app.roles r on r.id = m.role_id and r.tenant_id = m.tenant_id where m.tenant_id = ${lit(tenantId)} and m.platform_user_id = ${lit(user?.id)}`);
    s.dbCheck(membership !== null && membership.is_primary_administrator && membership.is_active, 'primary, active membership in the new tenant', membership && [membership.is_primary_administrator, membership.is_active]);
    s.dbCheck(membership?.role_name === 'Laboratory Administrator' && membership?.role_tenant === tenantId, 'role "Laboratory Administrator" in the new tenant', membership?.role_name);
    s.dbCheck(membership?.special_can_pay && membership?.special_can_refund && membership?.special_can_cancel && membership?.special_can_modify_verified_result && num(membership?.special_discount_cap_percent) === 100, 'role carries every special right, cap 100 %');
    const keys = membership?.permission_keys ?? [];
    s.dbCheck(Array.isArray(keys) && keys.length > 50 && !keys.some((key) => /^(platform|tenancy)\./.test(key)), `role holds ${keys.length} laboratory keys and no platform key`, keys.filter((key) => /^(platform|tenancy)\./.test(key)));
    ctx.adminRoleId = membership?.role_id;
    // master data copy (BR-25): every active row of the platform's global catalogue A arrives in the tenant
    const copied = (table, column = 'code') => {
      const source = rows(`select ${column} as k from app.${table} where tenant_id = ${lit(PLATFORM_TENANT)} and is_active order by 1`).map((row) => row.k);
      const target = rows(`select ${column} as k from app.${table} where tenant_id = ${lit(tenantId)} order by 1`).map((row) => row.k);
      s.dbCheck(source.every((key) => target.includes(key)), `master-data copy ${table}: [${source.join(',')}] → [${target.join(',')}]`);
    };
    copied('clinical_groups');
    copied('sample_types');
    copied('measurement_units', 'name');
    copied('tests');
    copied('services');
    const fkOk = scalar(`select count(*) as n from app.services s join app.clinical_groups g on g.id = s.clinical_group_id and g.tenant_id = s.tenant_id where s.tenant_id = ${lit(tenantId)}`) === scalar(`select count(*) as n from app.services where tenant_id = ${lit(tenantId)} and clinical_group_id is not null`);
    s.dbCheck(fkOk, 'copied services point at the tenant\'s own copied clinical groups (no platform ids)');
    const security = one(`select name, detail from audit.security_events where detail like ${lit(`tenant:${tenantId}%`)} order by at_utc desc limit 1`);
    s.dbCheck(security !== null, 'security event for the provisioning', security);
    await verifyAuditAndOutbox(s, response.correlation, tenantId, { fieldChanges: ['Branch:Created', 'Role:Created', 'TenantMembership:Created', 'PlatformUser:Created', 'Tenant:Created', 'Wallet:Created'], events: ['TenantProvisioned'] });
    verifyNoSecrets(s, response.correlation, [ctx.adminPassword]);
  });

  // ================================================================== 2. the laboratory administrator
  await step('A-01', `${ctx.adminUsername} signs in and selects branch MAIN`, async (s) => {
    need(ctx.tenant, 'tenant');
    s.req('POST sign-in; POST /api/v1/auth/select-branch {MAIN}');
    s.expect('200 signed-in in the new tenant; branch bound to the refresh session');
    const response = await admin.signIn(ctx.adminUsername, ctx.adminPassword);
    s.status(response, 200, 'sign-in');
    s.check(admin.profile?.tenantId === ctx.tenant, 'profile tenant = new tenant', admin.profile?.tenantId);
    s.check(admin.profile?.mustChangePassword !== true, 'no forced password change for the provisioned administrator', admin.profile?.mustChangePassword);
    const selected = await admin.post('/api/v1/auth/select-branch', { branchId: ctx.mainBranch });
    s.status(selected, 200, 'select-branch');
    s.check(selected.json?.branchId === ctx.mainBranch, 'profile.branchId = MAIN', selected.json?.branchId);
    const session = one(`select tenant_id, branch_id from identity.refresh_sessions where platform_user_id = ${lit(ctx.adminUserId)} and not is_revoked and rotated_at_utc is null order by created_at_utc desc limit 1`);
    s.dbCheck(session?.tenant_id === ctx.tenant && session?.branch_id === ctx.mainBranch, 'refresh_sessions tenant_id + branch_id', session);
  });

  await step('A-02', 'Refresh through the cookie endpoint (rotation)', async (s) => {
    need(admin.cookie, 'refresh cookie');
    s.req(`POST /api/v1/auth/refresh (cookie, Origin ${api.origin}, Sec-Fetch-Site same-origin)`);
    s.expect('200 new access token, cookie rotated, branch kept; the new token works');
    const before = admin.cookie;
    const response = await admin.cookiePost('/api/v1/auth/refresh');
    s.status(response, 200);
    admin.captureCookie(response);
    s.check(admin.cookie !== before, 'refresh cookie rotated');
    if (response.json?.accessToken) admin.token = response.json.accessToken;
    s.check(response.json?.profile?.branchId === ctx.mainBranch, 'branch inherited through rotation', response.json?.profile?.branchId);
    const me = await admin.get('/api/v1/auth/me');
    s.status(me, 200, 'me');
    const live = rows(`select id, branch_id, revoked_at_utc from identity.refresh_sessions where platform_user_id = ${lit(ctx.adminUserId)} and not is_revoked and rotated_at_utc is null`);
    s.dbCheck(live.length >= 1 && live.every((row) => row.branch_id === ctx.mainBranch), 'live refresh session keeps branch MAIN', live);
    const noOrigin = await api.request('POST', '/api/v1/auth/refresh', { cookie: admin.cookie });
    s.status(noOrigin, [401, 403], 'refresh without Origin/Sec-Fetch-Site');
  });

  await step('A-03', 'Tenant setting change: reviewBeforeVerify = true', async (s) => {
    s.req('PUT /api/v1/settings {settings:[{key:reviewBeforeVerify,value:true}]}');
    s.expect('200; app.tenant_settings row (tenant, key, value true, updated_by = admin)');
    const response = await admin.put('/api/v1/settings', { settings: [{ key: 'reviewBeforeVerify', value: 'true' }] });
    s.status(response, 200);
    const item = response.json?.settings?.find((setting) => setting.key === 'reviewBeforeVerify');
    s.check(item?.value === 'true' && item?.isDefault === false, 'response shows reviewBeforeVerify=true (not default)', item);
    const row = one(`select * from app.tenant_settings where tenant_id = ${lit(ctx.tenant)} and key = 'reviewBeforeVerify'`);
    s.dbCheck(row !== null && /true/i.test(String(row.value)) && row.updated_by_user_id === ctx.adminUserId, 'tenant_settings value true, updated_by_user_id = admin', row);
    await verifyAuditAndOutbox(s, response.correlation, ctx.tenant, {});
    s.dbCheck(rows(`select 1 from audit.field_changes where correlation_id = ${lit(response.correlation)}`).length > 0, 'the setting change is in audit.field_changes');
  });

  await step('A-04', 'Branch V11B2 created', async (s) => {
    s.req('POST /api/v1/organization/branches');
    s.expect('201; app.branches row in the tenant, code upper-cased, currency EGP');
    const response = await admin.post('/api/v1/organization/branches', { code: 'v11b2', nameEn: 'V11 Second Branch', nameAr: 'الفرع الثاني', countryId: null, governorateId: null, phone: '+20 2 1111 2222', email: 'b2@v11.example', currency: 'EGP' });
    s.status(response, 201);
    ctx.branch2 = response.json?.id;
    const row = one(`select * from app.branches where id = ${lit(ctx.branch2)}`);
    s.dbCheck(row?.tenant_id === ctx.tenant && row?.code === 'V11B2' && row?.currency === 'EGP' && row?.is_active === true && row?.created_by_user_id === ctx.adminUserId, 'branch row (tenant, code V11B2, EGP, active, created_by admin)', row);
    await verifyAuditAndOutbox(s, response.correlation, ctx.tenant, { fieldChanges: ['Branch:Created'], events: ['BranchCreated'] });
  });

  // ---- catalogue
  await step('C-01', 'Clinical group V11G, sample type V11S, unit V11-mmol/L', async (s) => {
    s.req('POST clinical-groups, sample-types, units');
    s.expect('201 ×3; rows in the tenant with the sent values');
    const group = await admin.post('/api/v1/catalogue/clinical-groups', { code: 'V11G', unitCode: 'V', nameEn: 'V11 Chemistry', nameAr: 'كيمياء في11', reportName: 'V11 Chemistry', reportTemplateId: null, displayOrder: 11 });
    s.status(group, 201, 'group');
    ctx.group = group.json?.id;
    const sample = await admin.post('/api/v1/catalogue/sample-types', { code: 'V11S', shortName: 'V11S', nameEn: 'V11 Serum', nameAr: 'مصل في11', conditions: [] });
    s.status(sample, 201, 'sample type');
    ctx.sampleType = sample.json?.id;
    const unit = await admin.post('/api/v1/catalogue/units', { name: 'V11-mmol/L' });
    s.status(unit, 201, 'unit');
    ctx.unit = unit.json?.id;
    const g = one(`select * from app.clinical_groups where id = ${lit(ctx.group)}`);
    s.dbCheck(g?.tenant_id === ctx.tenant && g?.code === 'V11G' && g?.unit_code === 'V' && g?.display_order === 11, 'clinical_groups row', g);
    const t = one(`select * from app.sample_types where id = ${lit(ctx.sampleType)}`);
    s.dbCheck(t?.tenant_id === ctx.tenant && t?.code === 'V11S' && t?.name_en === 'V11 Serum', 'sample_types row', t);
    const u = one(`select * from app.measurement_units where id = ${lit(ctx.unit)}`);
    s.dbCheck(u?.tenant_id === ctx.tenant && u?.name === 'V11-mmol/L' && u?.is_active, 'measurement_units row', u);
    await verifyAuditAndOutbox(s, group.correlation, ctx.tenant, { fieldChanges: ['ClinicalGroup:Created'], events: ['ClinicalGroupCreated'] });
    await verifyAuditAndOutbox(s, sample.correlation, ctx.tenant, { fieldChanges: ['SampleType:Created'], events: ['SampleTypeCreated'] });
    await verifyAuditAndOutbox(s, unit.correlation, ctx.tenant, { fieldChanges: ['MeasurementUnit:Created'], events: ['MeasurementUnitCreated'] });
  });

  await step('C-02', 'Numeric test V11K with range band 3.5–5.0 and panic 2.5/6.5', async (s) => {
    s.req('POST /catalogue/tests; PUT /tests/{id}/range-bands');
    s.expect('201 + 204; app.tests (Numeric, unit FK, group FK, decimals 1) + one app.test_range_bands row');
    const test = await admin.post('/api/v1/catalogue/tests', { code: 'V11K', nameEn: 'V11 Potassium', nameAr: 'بوتاسيوم', reportName: 'Potassium', loinc: null, cpt: null, clinicalGroupId: ctx.group, type: 'Numeric', sampleTypeId: ctx.sampleType, sampleConditionId: null, unitId: ctx.unit, gender: 'Any', methodology: 'ISE', decimals: 1, isHiddenHelper: false, printReference: true, labelCount: 1, prefix: null });
    s.status(test, 201, 'test');
    ctx.testK = test.json?.id;
    const bands = await admin.put(`/api/v1/catalogue/tests/${ctx.testK}/range-bands`, { bands: [{ sex: 'Any', ageFrom: 0, ageTo: 150, ageUnit: 'Years', patientConditionId: null, low: 3.5, high: 5.0, panicLow: 2.5, panicHigh: 6.5, reportableLow: null, reportableHigh: null, textRange: null }] });
    s.status(bands, 204, 'range-bands');
    const row = one(`select * from app.tests where id = ${lit(ctx.testK)}`);
    s.dbCheck(row?.tenant_id === ctx.tenant && row?.type === 'Numeric' && row?.unit_id === ctx.unit && row?.clinical_group_id === ctx.group && row?.sample_type_id === ctx.sampleType && row?.decimals === 1, 'tests row (Numeric, unit/group/sample FKs, decimals 1)', row && [row.type, row.unit_id, row.decimals]);
    const band = rows(`select * from app.test_range_bands where lab_test_id = ${lit(ctx.testK)}`);
    s.dbCheck(band.length === 1 && num(band[0].low) === 3.5 && num(band[0].high) === 5 && num(band[0].panic_low) === 2.5 && num(band[0].panic_high) === 6.5 && band[0].tenant_id === ctx.tenant, 'one range band 3.5–5.0, panic 2.5/6.5', band);
    await verifyAuditAndOutbox(s, test.correlation, ctx.tenant, { fieldChanges: ['LabTest:Created'], events: ['LabTestCreated'] });
    await verifyAuditAndOutbox(s, bands.correlation, ctx.tenant, { events: ['LabTestRangesChanged'] });
  });

  await step('C-03', 'List test V11L (RestrictedList) with three values', async (s) => {
    s.req('POST /catalogue/tests; PUT /tests/{id}/list-values');
    s.expect('201 + 204; app.test_list_values ×3 (one default, one critical)');
    const test = await admin.post('/api/v1/catalogue/tests', { code: 'V11L', nameEn: 'V11 Blood group screen', nameAr: 'فصيلة', reportName: null, loinc: null, cpt: null, clinicalGroupId: ctx.group, type: 'RestrictedList', sampleTypeId: ctx.sampleType, sampleConditionId: null, unitId: null, gender: 'Any', methodology: null, decimals: 0, isHiddenHelper: false, printReference: false, labelCount: 1, prefix: null });
    s.status(test, 201, 'test');
    ctx.testL = test.json?.id;
    const values = await admin.put(`/api/v1/catalogue/tests/${ctx.testL}/list-values`, { values: [
      { value: '.', isNormal: true, isCritical: false, isDefault: true, isPlaceholder: true, colour: null },
      { value: 'Negative', isNormal: true, isCritical: false, isDefault: false, isPlaceholder: false, colour: null },
      { value: 'Positive', isNormal: false, isCritical: true, isDefault: false, isPlaceholder: false, colour: '#d32f2f' },
    ] });
    s.status(values, 204, 'list-values');
    const list = rows(`select value, is_default, is_critical, tenant_id from app.test_list_values where lab_test_id = ${lit(ctx.testL)} order by display_order`);
    s.dbCheck(list.length === 3 && list.every((row) => row.tenant_id === ctx.tenant) && list.find((row) => row.value === 'Positive')?.is_critical === true, 'three list values, Positive critical', list);
    await verifyAuditAndOutbox(s, values.correlation, ctx.tenant, { events: ['LabTestListValuesChanged'] });
  });

  await step('C-04', 'Services V11K and V11L (type Test, one member each)', async (s) => {
    s.req('POST /catalogue/services ×2');
    s.expect('201 ×2; app.services + app.service_members (kind Test, member = test id)');
    const service = (code, testId, shortcut) => ({ code, type: 'Test', shortName: code, shortcutNo: shortcut, nameEn: `${code} service`, nameAr: `خدمة ${code}`, reportName: null, loinc: null, cpt: null, clinicalGroupId: ctx.group, sampleTypeId: ctx.sampleType, sampleConditionId: null, header: null, aliases: [`v11-${code.toLowerCase()}`], precaution: null, gender: 'Any', turnaroundHours: 24, labelCount: 1, printAlone: false, isOrderable: true, showOnWeb: false, profileSum: null, members: [{ kind: 'Test', memberId: testId, order: 1, inSum: false, visible: true }] });
    const k = await admin.post('/api/v1/catalogue/services', service('V11K', ctx.testK, 1101));
    s.status(k, 201, 'V11K');
    ctx.serviceK = k.json?.id;
    const l = await admin.post('/api/v1/catalogue/services', service('V11L', ctx.testL, 1102));
    s.status(l, 201, 'V11L');
    ctx.serviceL = l.json?.id;
    for (const [id, testId, code] of [[ctx.serviceK, ctx.testK, 'V11K'], [ctx.serviceL, ctx.testL, 'V11L']]) {
      const row = one(`select * from app.services where id = ${lit(id)}`);
      const members = rows(`select kind, member_id, tenant_id from app.service_members where service_id = ${lit(id)}`);
      s.dbCheck(row?.tenant_id === ctx.tenant && row?.code === code && row?.type === 'Test' && row?.clinical_group_id === ctx.group && row?.is_active, `services ${code} row`, row && [row.code, row.type]);
      s.dbCheck(members.length === 1 && members[0].member_id === testId && members[0].tenant_id === ctx.tenant, `service_members ${code} → test`, members);
    }
    await verifyAuditAndOutbox(s, k.correlation, ctx.tenant, { fieldChanges: ['Service:Created'], events: ['ServiceCreated'] });
  });

  // ---- reduced role + second user
  await step('O-01', 'Role "V11 Technician" with a reduced permission set, result rights on V11G, special right ModifyVerified', async (s) => {
    const keys = ['billing.view', 'catalogue.view', 'results.view', 'results.create', 'results.edit', 'results.approve', 'registration.view', 'patients.view'];
    s.req('POST /organization/roles; PUT roles/{id}/result-rights; PUT roles/{id}/special-rights');
    s.expect('201 + 204 + 204; app.roles (8 keys) + app.role_result_rights (V11G, S R V E P) + special_can_modify_verified_result');
    const role = await admin.post('/api/v1/organization/roles', { nameEn: 'V11 Technician', nameAr: 'فني في11', description: 'Validation role (tester D)', permissionKeys: keys });
    s.status(role, 201, 'role');
    ctx.techRole = role.json?.id;
    const rights = await admin.put(`/api/v1/organization/roles/${ctx.techRole}/result-rights`, { rights: [{ clinicalGroupId: ctx.group, branchId: null, canSee: true, canReview: true, canVerify: true, canEdit: true, canPrint: true }] });
    s.status(rights, 204, 'result-rights');
    const special = await admin.put(`/api/v1/organization/roles/${ctx.techRole}/special-rights`, { discountCapPercent: 0, canPay: false, canRefund: false, canTransfer: false, canCancel: false, canModifyVerifiedResult: true, canPreviewWithoutVerify: false, canPrintInvoice: false, canSeeVip: false });
    s.status(special, 204, 'special-rights');
    const row = one(`select * from app.roles where id = ${lit(ctx.techRole)}`);
    s.dbCheck(row?.tenant_id === ctx.tenant && row?.name_en === 'V11 Technician' && JSON.stringify([...(row?.permission_keys ?? [])].sort()) === JSON.stringify([...keys].sort()), 'roles row with exactly the 8 keys', row?.permission_keys);
    s.dbCheck(row?.special_can_modify_verified_result === true && row?.special_can_pay === false, 'special rights stored (modify verified only)', row && [row.special_can_modify_verified_result, row.special_can_pay]);
    const rr = rows(`select * from app.role_result_rights where role_id = ${lit(ctx.techRole)}`);
    s.dbCheck(rr.length === 1 && rr[0].clinical_group_id === ctx.group && rr[0].can_verify && rr[0].tenant_id === ctx.tenant, 'role_result_rights row (V11G, verify)', rr);
    await verifyAuditAndOutbox(s, role.correlation, ctx.tenant, { fieldChanges: ['Role:Created'], events: ['RoleCreated'] });
  });

  await step('O-02', `User ${ctx.techUsername} created on branch MAIN with the reduced role (sealed password)`, async (s) => {
    s.req('POST /organization/users {password: JWE}');
    s.expect('201; platform_users + tenant_memberships (role V11 Technician, branch_ids [MAIN])');
    const response = await admin.postSealed('/api/v1/organization/users', { username: ctx.techUsername, password: ctx.techPassword, mustChangePassword: false, fullNameEn: 'V11 Technician', fullNameAr: 'فني', email: `${ctx.techUsername}@v11.example`, mobile: null, designation: 'Technician', shifts: null, roleId: ctx.techRole, branchIds: [ctx.mainBranch], passwordExpiresAtUtc: null, employeeCode: null, preferredLanguage: 'en' }, ['password']);
    s.status(response, 201);
    recordCredentials([{ username: ctx.techUsername, password: ctx.techPassword, role: 'V11 Technician (billing view, results; script)', tenant: `${ctx.clientName} (${ctx.tenant})` }]);
    const user = one(`select id, is_active, must_change_password from identity.platform_users where username = ${lit(ctx.techUsername)}`);
    ctx.techUserId = user?.id;
    const membership = one(`select * from app.tenant_memberships where tenant_id = ${lit(ctx.tenant)} and platform_user_id = ${lit(user?.id)}`);
    s.dbCheck(user?.is_active === true && user?.must_change_password === false, 'platform_users row active, no forced change', user);
    s.dbCheck(membership?.role_id === ctx.techRole && JSON.stringify(membership?.branch_ids) === JSON.stringify([ctx.mainBranch]) && membership?.is_primary_administrator === false, 'membership role + branch_ids [MAIN]', membership && [membership.role_id, membership.branch_ids]);
    await verifyAuditAndOutbox(s, response.correlation, ctx.tenant, { fieldChanges: ['TenantMembership:Created'], events: ['TenantMembershipCreated'], actor: ctx.adminUserId });
    verifyNoSecrets(s, response.correlation, [ctx.techPassword]);
  });

  // ---- pricing
  await step('R-01', 'Base price list V11-2026: lines V11K 150, V11L 100, activated', async (s) => {
    s.req('POST /pricing/base-price-lists; PUT {id}/lines; POST {id}/activate');
    s.expect('201, 200 {added:2}, 204; base_price_lists active + 2 base_price_lines');
    const list = await admin.post('/api/v1/pricing/base-price-lists', { code: 'v11-2026', nameEn: 'V11 list 2026', nameAr: 'قائمة في11', year: 2026 });
    s.status(list, 201, 'list');
    ctx.priceList = list.json?.id;
    const lines = await admin.put(`/api/v1/pricing/base-price-lists/${ctx.priceList}/lines`, { lines: [{ serviceId: ctx.serviceK, price: 150, isActive: true }, { serviceId: ctx.serviceL, price: 100, isActive: true }] });
    s.status(lines, 200, 'lines');
    s.check(lines.json?.added === 2, 'two lines added', lines.json);
    const activate = await admin.post(`/api/v1/pricing/base-price-lists/${ctx.priceList}/activate`);
    s.status(activate, 204, 'activate');
    const row = one(`select * from app.base_price_lists where id = ${lit(ctx.priceList)}`);
    s.dbCheck(row?.tenant_id === ctx.tenant && row?.code === 'V11-2026' && row?.is_active === true && row?.year === 2026, 'list row (V11-2026, active, 2026)', row && [row.code, row.is_active]);
    const prices = rows(`select service_id, price, is_active, tenant_id from app.base_price_lines where price_list_id = ${lit(ctx.priceList)}`);
    const priceOf = (id) => num(prices.find((row) => row.service_id === id)?.price);
    s.dbCheck(prices.length === 2 && priceOf(ctx.serviceK) === 150 && priceOf(ctx.serviceL) === 100 && prices.every((row) => row.tenant_id === ctx.tenant), 'lines 150 / 100 in the tenant', prices);
    await verifyAuditAndOutbox(s, activate.correlation, ctx.tenant, {});
  });

  await step('R-02', 'Cash payer (default) and payer "V11 Corporate" with tier "V11 Gold"', async (s) => {
    s.req('POST /pricing/payers/default; POST /pricing/payers; POST /pricing/payers/{id}/tiers');
    s.expect('200 {created:true}; 201; 201; payers + contract_tiers rows (FK payer, tenant)');
    const cash = await admin.post('/api/v1/pricing/payers/default');
    s.status(cash, [200, 201], 'default');
    ctx.cashPayer = cash.json?.payerId;
    ctx.cashTier = cash.json?.contractTierId;
    s.check(cash.json?.created === true, 'cash payer created now', cash.json);
    const payer = await admin.post('/api/v1/pricing/payers', { nameEn: 'V11 Corporate', nameAr: 'شركة في11', searchKey: 'v11corp', type: 'Credit', webPortalEnabled: false, webUsername: null });
    s.status(payer, 201, 'payer');
    ctx.payer = payer.json?.id;
    const tier = await admin.post(`/api/v1/pricing/payers/${ctx.payer}/tiers`, { nameEn: 'V11 Gold', nameAr: 'ذهبي' });
    s.status(tier, 201, 'tier');
    ctx.tier = tier.json?.id;
    const cashRow = one(`select p.type, p.is_default, t.id as tier_id, t.is_default as tier_default from app.payers p join app.contract_tiers t on t.payer_id = p.id and t.tenant_id = p.tenant_id where p.id = ${lit(ctx.cashPayer)} and p.tenant_id = ${lit(ctx.tenant)}`);
    s.dbCheck(cashRow?.type === 'Cash' && cashRow?.is_default && cashRow?.tier_id === ctx.cashTier, 'cash payer (Cash, default) with its default tier', cashRow);
    const payerRow = one(`select * from app.payers where id = ${lit(ctx.payer)}`);
    s.dbCheck(payerRow?.tenant_id === ctx.tenant && payerRow?.search_key === 'V11CORP' && payerRow?.type === 'Credit', 'payer row (search key upper-cased, Credit)', payerRow && [payerRow.search_key, payerRow.type]);
    const tierRow = one(`select * from app.contract_tiers where id = ${lit(ctx.tier)}`);
    s.dbCheck(tierRow?.payer_id === ctx.payer && tierRow?.tenant_id === ctx.tenant && tierRow?.name_en === 'V11 Gold', 'tier row → payer', tierRow);
    await verifyAuditAndOutbox(s, payer.correlation, ctx.tenant, { fieldChanges: ['Payer:Created'], events: ['PayerCreated'] });
    await verifyAuditAndOutbox(s, tier.correlation, ctx.tenant, { events: ['ContractTierAdded'] });
  });

  // ---- the treasury is withdrawn (LIS-CR-004, 2026-10-20): money is taken by the session user on the session branch
  await step('T-01', 'No treasury: the treasury endpoints are gone', async (s) => {
    s.req('GET /api/v1/treasuries; GET /api/v1/treasury-access-requests; GET /api/v1/billing/my-treasuries');
    s.expect('404 on each (the module is removed; the tables stay as history)');
    for (const route of ['/api/v1/treasuries', '/api/v1/treasury-access-requests', '/api/v1/billing/my-treasuries?deviceId=any']) {
      const response = await admin.get(route);
      s.status(response, 404, route);
    }
  });

  await step('T-02', 'The technician signs in; the session is bound to its only branch', async (s) => {
    s.req('POST /api/v1/auth/sign-in as tech');
    s.expect('200; profile.branchId = MAIN');
    const signIn = await tech.signIn(ctx.techUsername, ctx.techPassword);
    s.status(signIn, 200, 'tech sign-in');
    s.check(tech.profile?.branchId === ctx.mainBranch, 'tech session auto-bound to its only branch MAIN', tech.profile?.branchId);
  });

  await step('T-03', 'Refund reason "V11 Overpayment" (kind Refund)', async (s) => {
    s.req('POST /api/v1/catalogue/reasons');
    s.expect('201; app.reasons kind Refund');
    const response = await admin.post('/api/v1/catalogue/reasons', { kind: 'Refund', nameEn: 'V11 Overpayment', nameAr: 'مبلغ زائد' });
    s.status(response, 201);
    ctx.refundReason = response.json?.id;
    const row = one(`select * from app.reasons where id = ${lit(ctx.refundReason)}`);
    s.dbCheck(row?.tenant_id === ctx.tenant && ['Refund', '3', 3].includes(row?.kind) && row?.name_en === 'V11 Overpayment', 'reasons row (Refund)', row && [row.kind, row.name_en]);
    await verifyAuditAndOutbox(s, response.correlation, ctx.tenant, { fieldChanges: ['Reason:Created'], actor: ctx.adminUserId });
  });

  // ---- patient and registration
  await step('G-01', 'Patient "V11 Mona Validation" registered', async (s) => {
    s.req('POST /api/v1/patients');
    s.expect('201 {id, number}; app.patients row with the sent demographics');
    const response = await admin.post('/api/v1/patients', { title: 'Mrs', fullName: 'V11 Mona Validation', nameAr: 'منى', gender: 'Female', dateOfBirth: '1990-05-17', age: null, patientConditionId: null, mobile: `010${randomDigits(8)}`, nationalId: null, nationality: 'Egyptian', email: null, governorateId: null, city: 'Cairo', address: null, comments: 'created by tests/validation/backend-cycle.mjs', isVip: null });
    s.status(response, 201);
    ctx.patient = response.json?.id;
    ctx.patientNumber = response.json?.number;
    const row = one(`select * from app.patients where id = ${lit(ctx.patient)}`);
    s.dbCheck(row?.tenant_id === ctx.tenant && row?.number === ctx.patientNumber && row?.full_name === 'V11 Mona Validation' && row?.gender === 'Female' && row?.date_of_birth === '1990-05-17' && row?.status === 'Active', `patients row ${ctx.patientNumber}`, row && [row.number, row.full_name, row.gender, row.date_of_birth, row.status]);
    await verifyAuditAndOutbox(s, response.correlation, ctx.tenant, { fieldChanges: ['Patient:Created'], events: ['PatientRegistered'], actor: ctx.adminUserId });
  });

  await step('V-01', 'Visit registered (cash payer, V11K + V11L = 250 EGP); samples and results created by the outbox policies', async (s) => {
    s.req('POST /api/v1/registration/visits');
    s.expect('201 ACC-…; visit Open, totals 250/250/0, 2 lines, 1 tube; policies create samples (Received) and 2 results');
    const response = await admin.post('/api/v1/registration/visits', { patientId: ctx.patient, treatmentNo: null, referringDoctorId: null, payerId: ctx.cashPayer, contractTierId: ctx.cashTier, insuranceId: null, diagnosisId: null, bookingId: null, urgent: false, biohazard: false, collectedOutside: false, lines: [{ serviceId: ctx.serviceK, reserved: false }, { serviceId: ctx.serviceL, reserved: false }], preparationAnswers: null, discount: null, confirmFrequencyWarnings: false, utcOffsetMinutes: 180 });
    s.status(response, 201);
    ctx.visit = response.json?.id;
    ctx.accession = response.json?.accession;
    s.check(/^ACC-/.test(ctx.accession ?? ''), 'accession ACC-…', ctx.accession);
    s.check(response.json?.tubeCount === 1, 'one tube', response.json?.tubeCount);
    const visit = one(`select * from app.visits where id = ${lit(ctx.visit)}`);
    s.dbCheck(visit?.tenant_id === ctx.tenant && visit?.accession === ctx.accession && visit?.patient_id === ctx.patient && visit?.branch_id === ctx.mainBranch && visit?.payer_id === ctx.cashPayer && visit?.contract_tier_id === ctx.cashTier && visit?.registered_by_user_id === ctx.adminUserId, 'visit row FKs (tenant, patient, branch, payer, tier, registered_by)', visit && [visit.accession, visit.branch_id]);
    s.dbCheck(visit?.status === 'Open' && num(visit?.total_list_price) === 250 && num(visit?.patient_share_total) === 250 && num(visit?.payer_share_total) === 0 && visit?.utc_offset_minutes === 180, 'visit Open, totals 250 / 250 / 0, offset 180', visit && [visit.status, visit.total_list_price, visit.patient_share_total, visit.payer_share_total]);
    const lines = rows(`select id, service_id, status, list_price, patient_share, tube_id, tenant_id from app.visit_lines where visit_id = ${lit(ctx.visit)}`);
    const lineOf = (id) => lines.find((line) => line.service_id === id);
    ctx.lineK = lineOf(ctx.serviceK)?.id;
    ctx.lineL = lineOf(ctx.serviceL)?.id;
    s.dbCheck(lines.length === 2 && num(lineOf(ctx.serviceK)?.patient_share) === 150 && num(lineOf(ctx.serviceL)?.patient_share) === 100 && lines.every((line) => line.tenant_id === ctx.tenant && line.tube_id), 'two lines priced 150 / 100 with a tube', lines.map((line) => [line.status, line.patient_share]));
    const tubes = rows(`select id, sample_number, clinical_group_id, sample_type_id from app.visit_tubes where visit_id = ${lit(ctx.visit)}`);
    s.dbCheck(tubes.length === 1 && tubes[0].clinical_group_id === ctx.group && tubes[0].sample_type_id === ctx.sampleType, 'one tube V11G / V11S', tubes);
    ctx.sampleNumber = tubes[0]?.sample_number;
    await verifyAuditAndOutbox(s, response.correlation, ctx.tenant, { fieldChanges: ['Visit:Created'], events: ['VisitRegistered'], actor: ctx.adminUserId });
    const samples = await poll(() => rows(`select status, tube_id, sample_number, tenant_id from app.samples where visit_id = ${lit(ctx.visit)}`), (list) => list.length === 1 && list[0].status === 'Received');
    s.dbCheck(samples.ok && samples.value[0]?.tube_id === tubes[0]?.id && samples.value[0]?.tenant_id === ctx.tenant, `sample created by the outbox policy and Received (autoCollect/autoReceive) in ${(samples.ms / 1000).toFixed(1)} s`, samples.value);
    const results = await poll(() => rows(`select id, test_id, line_id, status, tenant_id, branch_id, clinical_group_id from app.results where visit_id = ${lit(ctx.visit)}`), (list) => list.length === 2);
    s.dbCheck(results.ok && results.value.every((row) => row.tenant_id === ctx.tenant && row.branch_id === ctx.mainBranch && row.clinical_group_id === ctx.group && row.status === 'Ordered'), `2 results Ordered created by the outbox policy in ${(results.ms / 1000).toFixed(1)} s`, results.value);
    ctx.resultK = results.value.find((row) => row.test_id === ctx.testK)?.id;
    ctx.resultL = results.value.find((row) => row.test_id === ctx.testL)?.id;
    s.dbCheck(results.value.find((row) => row.test_id === ctx.testK)?.line_id === ctx.lineK, 'result V11K → line V11K');
  });

  // ---- billing
  await step('B-01', 'Partial payment 100 EGP cash at registration', async (s) => {
    s.req('POST /api/v1/billing/payments {visit, 100, Cash}');
    s.expect('201 RCT-…; payments row Posted (cashier admin, branch MAIN, no treasury, no device); visit_accounts paid 100 → Partial');
    const response = await admin.post('/api/v1/billing/payments', { visitId: ctx.visit, amount: 100, method: 'Cash', reference: null });
    s.status(response, 201);
    ctx.payment1 = response.json?.id;
    s.check(/^RCT-/.test(response.json?.receiptNumber ?? ''), 'receipt RCT-…', response.json?.receiptNumber);
    const row = one(`select * from app.payments where id = ${lit(ctx.payment1)}`);
    s.dbCheck(row?.tenant_id === ctx.tenant && row?.visit_id === ctx.visit && num(row?.amount) === 100 && row?.method === 'Cash' && row?.status === 'Posted' && row?.cashier_user_id === ctx.adminUserId && row?.branch_id === ctx.mainBranch && row?.treasury_id === null && row?.device_id === null && row?.receipt_number === response.json?.receiptNumber, 'payments row (100 Cash Posted, cashier, branch, no treasury, no device, receipt)', row && [row.amount, row.status, row.receipt_number]);
    const account = one(`select * from app.visit_accounts where id = ${lit(ctx.visit)}`);
    s.dbCheck(num(account?.paid_total) === 100 && num(account?.refunded_total) === 0 && account?.tenant_id === ctx.tenant, 'visit_accounts paid_total 100', account && [account.paid_total, account.refunded_total]);
    const view = await admin.get(`/api/v1/billing/visits/${ctx.visit}/account`);
    s.check(view.json?.balanceStatus === 'Partial' && num(view.json?.remaining) === 150, 'account view Partial, remaining 150', view.json && [view.json.balanceStatus, view.json.remaining]);
    await verifyAuditAndOutbox(s, response.correlation, ctx.tenant, { fieldChanges: ['Payment:Created'], events: ['PaymentRecorded'], actor: ctx.adminUserId });
  });

  await step('B-02', 'Second payment 150 EGP card → Paid', async (s) => {
    s.req('POST /api/v1/billing/payments {150, Card, SLIP-V11}');
    s.expect('201; paid_total 250, remaining 0, Paid');
    const response = await admin.post('/api/v1/billing/payments', { visitId: ctx.visit, amount: 150, method: 'Card', reference: 'SLIP-V11' });
    s.status(response, 201);
    ctx.payment2 = response.json?.id;
    const row = one(`select amount, method, reference, status from app.payments where id = ${lit(ctx.payment2)}`);
    s.dbCheck(num(row?.amount) === 150 && row?.method === 'Card' && row?.reference === 'SLIP-V11' && row?.status === 'Posted', 'payments row 150 Card SLIP-V11', row);
    const account = one(`select paid_total from app.visit_accounts where id = ${lit(ctx.visit)}`);
    s.dbCheck(num(account?.paid_total) === 250, 'visit_accounts paid_total 250', account);
    const view = await admin.get(`/api/v1/billing/visits/${ctx.visit}/account`);
    s.check(view.json?.balanceStatus === 'Paid' && num(view.json?.remaining) === 0 && view.json?.payments?.length === 2, 'account view Paid, remaining 0, 2 payments', view.json && [view.json.balanceStatus, view.json.remaining]);
    await verifyAuditAndOutbox(s, response.correlation, ctx.tenant, { events: ['PaymentRecorded'] });
  });

  // ---- results
  await step('E-01', 'Technician enters V11K = 7.2 → CriticalHigh, panic', async (s) => {
    s.req('GET /api/v1/results?accession=…; PUT /api/v1/results/{id} {numericValue 7.2}');
    s.expect('200 case with 2 rows; 204; results Complete, 7.2, flag CriticalHigh, is_panic, range copied, entered_by tech; ResultPanicDetected processed');
    const view = await tech.get(`/api/v1/results?accession=${encodeURIComponent(ctx.accession)}`);
    s.status(view, 200, 'case');
    const unit = view.json?.units?.[0];
    s.check(view.json?.units?.length === 1 && unit?.results?.length === 2, 'one report unit with two rows', view.json?.units?.map((u) => u.results.length));
    s.check(view.json?.settings?.reviewBeforeVerify === true, 'case settings reflect reviewBeforeVerify=true (A-03)', view.json?.settings);
    const response = await tech.put(`/api/v1/results/${ctx.resultK}`, { numericValue: 7.2, textValue: null, paragraphs: null, culture: null });
    s.status(response, 204);
    const row = one(`select * from app.results where id = ${lit(ctx.resultK)}`);
    s.dbCheck(row?.status === 'Complete' && num(row?.numeric_value) === 7.2 && row?.flag === 'CriticalHigh' && row?.is_panic === true && row?.entered_by_user_id === ctx.techUserId, 'results Complete 7.2 CriticalHigh panic, entered_by tech', row && [row.status, row.numeric_value, row.flag, row.is_panic]);
    s.dbCheck(num(row?.range_low) === 3.5 && num(row?.range_high) === 5 && num(row?.range_panic_high) === 6.5 && row?.unit === 'V11-mmol/L', 'range + unit snapshotted on the result', row && [row.range_low, row.range_high, row.range_panic_high, row.unit]);
    const log = rows(`select kind, value_after, by_user_id from app.result_log_entries where result_id = ${lit(ctx.resultK)} order by sequence`);
    s.dbCheck(log.some((entry) => entry.kind === 'Value' && entry.value_after === '7.2' && entry.by_user_id === ctx.techUserId), 'result_log_entries Value 7.2 by tech', log);
    await verifyAuditAndOutbox(s, response.correlation, ctx.tenant, { fieldChanges: ['Result:Modified'], events: ['ResultEntered', 'ResultPanicDetected'], actor: ctx.techUserId });
  });

  await step('E-02', 'Verify before review is refused (reviewBeforeVerify); review; verify', async (s) => {
    s.req('POST /results/{id}/verify, POST /results/{id}/review, POST /results/{id}/verify');
    s.expect('409/422 RES-016 (unchanged); 204 Reviewed (reviewed_by tech); 204 Verified (verified_by tech)');
    const early = await tech.post(`/api/v1/results/${ctx.resultK}/verify`);
    s.status(early, [409, 422], 'verify before review');
    s.code(early, 'RES-016');
    s.dbCheck(scalar(`select status from app.results where id = ${lit(ctx.resultK)}`) === 'Complete', 'still Complete after the refusal');
    const review = await tech.post(`/api/v1/results/${ctx.resultK}/review`);
    s.status(review, 204, 'review');
    const reviewed = one(`select status, reviewed_by_user_id from app.results where id = ${lit(ctx.resultK)}`);
    s.dbCheck(reviewed?.status === 'Reviewed' && reviewed?.reviewed_by_user_id === ctx.techUserId, 'Reviewed by tech', reviewed);
    await verifyAuditAndOutbox(s, review.correlation, ctx.tenant, { events: ['ResultReviewed'] });
    const verify = await tech.post(`/api/v1/results/${ctx.resultK}/verify`);
    s.status(verify, 204, 'verify');
    const verified = one(`select status, verified_by_user_id, verified_by_name, verified_at_utc from app.results where id = ${lit(ctx.resultK)}`);
    s.dbCheck(verified?.status === 'Verified' && verified?.verified_by_user_id === ctx.techUserId && verified?.verified_at_utc !== null, 'Verified by tech (with timestamp)', verified);
    await verifyAuditAndOutbox(s, verify.correlation, ctx.tenant, { events: ['ResultVerified'], actor: ctx.techUserId });
  });

  await step('E-03', 'Amend the verified V11K result to 4.6 with a reason', async (s) => {
    s.req('POST /results/{id}/amend {numericValue 4.6, reason ""} then with a reason');
    s.expect('400 VALIDATION-400 without reason; 204 → value 4.6, flag Normal, not panic, amendment_count 1, log + field change carry the reason');
    const noReason = await tech.post(`/api/v1/results/${ctx.resultK}/amend`, { numericValue: 4.6, textValue: null, paragraphs: null, culture: null, reason: '' });
    s.status(noReason, 400, 'no reason');
    s.code(noReason, 'VALIDATION-400');
    const reason = 'V11 transcription error';
    const response = await tech.post(`/api/v1/results/${ctx.resultK}/amend`, { numericValue: 4.6, textValue: null, paragraphs: null, culture: null, reason });
    s.status(response, 204, 'amend');
    const row = one(`select status, numeric_value, flag, is_panic, amendment_count from app.results where id = ${lit(ctx.resultK)}`);
    s.dbCheck(num(row?.numeric_value) === 4.6 && row?.flag === 'Normal' && row?.is_panic === false && row?.amendment_count === 1, `results 4.6 Normal, not panic, amendment_count 1 (status ${row?.status})`, row);
    const log = rows(`select kind, value_before, value_after, detail from app.result_log_entries where result_id = ${lit(ctx.resultK)} order by sequence`);
    s.dbCheck(log.some((entry) => entry.value_before === '7.2' && entry.value_after === '4.6' && entry.detail === reason), 'result_log_entries 7.2 → 4.6 with the reason', log.slice(-2));
    await verifyAuditAndOutbox(s, response.correlation, ctx.tenant, { fieldChanges: ['Result:Modified'], events: ['ResultAmended'], reason, actor: ctx.techUserId });
  });

  // ---- cancellation and refund
  await step('V-02', 'Cancel service V11L with a reason → patient share 150, refund due 100; its result cancelled by policy', async (s) => {
    s.req('POST /registration/visits/{id}/lines/cancel {lineIds:[V11L], reason}');
    s.expect('204; line Cancelled with reason + actor; visit patient_share_total 150; account RefundDue 100; result V11L Cancelled via outbox');
    const reason = 'V11 ordered by mistake';
    const response = await admin.post(`/api/v1/registration/visits/${ctx.visit}/lines/cancel`, { lineIds: [ctx.lineL], reasonId: null, reason });
    s.status(response, 204);
    const line = one(`select status, cancellation_reason, cancelled_by_user_id, cancelled_at_utc from app.visit_lines where id = ${lit(ctx.lineL)}`);
    s.dbCheck(line?.status === 'Cancelled' && line?.cancellation_reason === reason && line?.cancelled_by_user_id === ctx.adminUserId && line?.cancelled_at_utc !== null, 'line Cancelled, reason + actor + time', line);
    const visit = one(`select status, total_list_price, patient_share_total from app.visits where id = ${lit(ctx.visit)}`);
    s.dbCheck(num(visit?.patient_share_total) === 150, `visit patient_share_total 150 (status ${visit?.status}, list ${visit?.total_list_price})`, visit);
    const view = await admin.get(`/api/v1/billing/visits/${ctx.visit}/account`);
    s.check(view.json?.balanceStatus === 'RefundDue' && num(view.json?.remaining) === -100 && num(view.json?.refundable) === 100, 'account RefundDue, remaining -100, refundable 100', view.json && [view.json.balanceStatus, view.json.remaining, view.json.refundable]);
    await verifyAuditAndOutbox(s, response.correlation, ctx.tenant, { fieldChanges: ['Visit:Modified'], events: ['VisitLineCancelled'], reason, actor: ctx.adminUserId });
    const result = await poll(() => one(`select status, cancelled_at_utc, cancellation_reason from app.results where id = ${lit(ctx.resultL)}`), (row) => row?.status === 'Cancelled');
    s.dbCheck(result.ok, `result V11L Cancelled by the outbox policy in ${(result.ms / 1000).toFixed(1)} s`, result.value);
    const unaffected = one(`select status from app.results where id = ${lit(ctx.resultK)}`);
    s.dbCheck(unaffected?.status !== 'Cancelled', 'result V11K not affected', unaffected);
  });

  await step('B-03', 'Refund the negative balance: 100 EGP cash with reason', async (s) => {
    s.req('POST /api/v1/billing/refunds {100, Cash, reason V11 Overpayment}');
    s.expect('201 RFD-…; refunds row (reason FK, cashier, branch, no treasury); refunded_total 100 → Paid, remaining 0');
    const response = await admin.post('/api/v1/billing/refunds', { visitId: ctx.visit, amount: 100, method: 'Cash', reference: null, reasonId: ctx.refundReason, note: 'V11 refund after cancellation' });
    s.status(response, 201);
    ctx.refund = response.json?.id;
    s.check(/^RFD-/.test(response.json?.number ?? ''), 'number RFD-…', response.json?.number);
    const row = one(`select * from app.refunds where id = ${lit(ctx.refund)}`);
    s.dbCheck(row?.tenant_id === ctx.tenant && row?.visit_id === ctx.visit && num(row?.amount) === 100 && row?.reason_id === ctx.refundReason && row?.note === 'V11 refund after cancellation' && row?.cashier_user_id === ctx.adminUserId && row?.branch_id === ctx.mainBranch && row?.treasury_id === null && row?.device_id === null && row?.number === response.json?.number, 'refunds row (100, reason FK, note, cashier, branch, no treasury)', row && [row.amount, row.number]);
    const account = one(`select paid_total, refunded_total from app.visit_accounts where id = ${lit(ctx.visit)}`);
    s.dbCheck(num(account?.paid_total) === 250 && num(account?.refunded_total) === 100, 'visit_accounts paid 250, refunded 100', account);
    const view = await admin.get(`/api/v1/billing/visits/${ctx.visit}/account`);
    s.check(view.json?.balanceStatus === 'Paid' && num(view.json?.remaining) === 0, 'account Paid, remaining 0', view.json && [view.json.balanceStatus, view.json.remaining]);
    const over = await admin.post('/api/v1/billing/refunds', { visitId: ctx.visit, amount: 1, method: 'Cash', reference: null, reasonId: ctx.refundReason, note: null });
    s.status(over, 409, 'refund beyond the refundable');
    s.code(over, 'BIL-011');
    await verifyAuditAndOutbox(s, response.correlation, ctx.tenant, { fieldChanges: ['Refund:Created'], events: ['RefundRecorded'], actor: ctx.adminUserId });
  });

  // ================================================================== 3. negative and integrity checks
  await step('N-01', 'Validation: patient without a name → 400 problem details', async (s) => {
    s.req('POST /api/v1/patients {fullName: ""}');
    s.expect('400 application/problem+json, code VALIDATION-400, errors.FullName, correlationId echoed; no row');
    const before = scalar(`select count(*) as n from app.patients where tenant_id = ${lit(ctx.tenant)}`);
    const response = await admin.post('/api/v1/patients', { title: 'Mr', fullName: '', nameAr: null, gender: 'Male', dateOfBirth: null, age: null, patientConditionId: null, mobile: null, nationalId: null, nationality: null, email: null, governorateId: null, city: null, address: null, comments: null, isVip: null });
    s.status(response, 400);
    s.code(response, 'VALIDATION-400');
    s.check(/problem\+json/.test(response.headers['content-type'] ?? ''), 'content-type application/problem+json', response.headers['content-type']);
    s.check(response.json?.status === 400 && typeof response.json?.title === 'string', 'problem details status + title', response.json);
    s.check(response.json?.correlationId === response.correlation, 'correlationId = X-Correlation-Id', response.json?.correlationId);
    s.check(response.json?.errors && Object.keys(response.json.errors).some((key) => /fullname/i.test(key)), 'errors names FullName', response.json?.errors);
    s.dbCheck(scalar(`select count(*) as n from app.patients where tenant_id = ${lit(ctx.tenant)}`) === before, 'no patient row written');
  });

  await step('N-02', 'Duplicates: sequential duplicate unit → module code; 4 concurrent identical creates → one row, the rest 409 (UNIQUE-409 when the index decides)', async (s) => {
    s.req('POST /catalogue/units {V11-mmol/L} again; 4× concurrent POST /catalogue/clinical-groups {V11RACE}');
    s.expect('409 CAT-102; exactly one 201, others 409 (CAT-… or UNIQUE-409), one DB row');
    const again = await admin.post('/api/v1/catalogue/units', { name: 'v11-MMOL/L' });
    s.status(again, 409, 'sequential duplicate');
    s.code(again, 'CAT-102');
    const race = await Promise.all(Array.from({ length: 4 }, () => admin.post('/api/v1/catalogue/clinical-groups', { code: 'V11RACE', unitCode: 'R', nameEn: 'V11 Race', nameAr: 'سباق', reportName: null, reportTemplateId: null, displayOrder: 99 })));
    const created = race.filter((response) => response.status === 201);
    const conflicts = race.filter((response) => response.status === 409);
    const codes = conflicts.map((response) => response.json?.code);
    s.saw(`concurrent: ${race.map((response) => `${response.status}${response.json?.code ? ` ${response.json.code}` : ''}`).join(', ')}`);
    s.check(created.length === 1 && conflicts.length === 3, 'one 201 and three 409', race.map((response) => response.status));
    s.check(codes.every((code) => code === 'UNIQUE-409' || /^CAT-/.test(code ?? '')), 'conflicts carry UNIQUE-409 or a CAT code', codes);
    ctx.uniqueObserved = codes.includes('UNIQUE-409');
    s.dbCheck(scalar(`select count(*) as n from app.clinical_groups where tenant_id = ${lit(ctx.tenant)} and code = 'V11RACE'`) === 1, 'exactly one V11RACE row');
  });

  await step('N-03', 'Stale concurrency token: patient update with an old rowVersion → CONCURRENCY-409', async (s) => {
    s.req('GET /patients/{id} (rowVersion v1); PUT with v1 → 204; PUT again with v1');
    s.expect('204 then 409 CONCURRENCY-409; the second write is not applied');
    const detail = await admin.get(`/api/v1/patients/${ctx.patient}`);
    s.status(detail, 200, 'detail');
    const v1 = detail.json?.rowVersion;
    s.check(typeof v1 === 'number' && v1 > 0, 'detail carries rowVersion', v1);
    s.dbCheck(String(scalar(`select xmin::text::bigint as x from app.patients where id = ${lit(ctx.patient)}`)) === String(v1), 'rowVersion = xmin of the row');
    const base = { title: 'Mrs', fullName: 'V11 Mona Validation', nameAr: 'منى', gender: 'Female', dateOfBirth: '1990-05-17', age: null, patientConditionId: null, mobile: detail.json?.mobile ?? null, nationalId: null, nationality: 'Egyptian', email: null, governorateId: null, city: 'Giza', address: null, comments: 'edited by the validation script', isVip: null };
    const first = await admin.put(`/api/v1/patients/${ctx.patient}`, { ...base, rowVersion: v1 });
    s.status(first, 204, 'first update');
    const stale = await admin.put(`/api/v1/patients/${ctx.patient}`, { ...base, city: 'Alexandria', rowVersion: v1 });
    s.status(stale, 409, 'stale update');
    s.code(stale, 'CONCURRENCY-409');
    const row = one(`select city from app.patients where id = ${lit(ctx.patient)}`);
    s.dbCheck(row?.city === 'Giza', 'city stays Giza (stale write not applied)', row);
    await verifyAuditAndOutbox(s, first.correlation, ctx.tenant, { fieldChanges: ['Patient:Modified'] });
  });

  await step('N-04', 'Permission: the technician (reduced role) cannot create a payer or open the settings', async (s) => {
    s.req('POST /pricing/payers as tech; PUT /settings as tech');
    s.expect('403 AUTHZ-001 ×2; nothing written');
    const payer = await tech.post('/api/v1/pricing/payers', { nameEn: 'V11 Forbidden', nameAr: 'ممنوع', searchKey: 'v11forbidden', type: 'Cash', webPortalEnabled: false, webUsername: null });
    s.status(payer, 403, 'payer');
    s.code(payer, 'AUTHZ-001');
    const settings = await tech.put('/api/v1/settings', { settings: [{ key: 'reviewBeforeVerify', value: 'false' }] });
    s.status(settings, 403, 'settings');
    s.code(settings, 'AUTHZ-001');
    s.dbCheck(scalar(`select count(*) as n from app.payers where tenant_id = ${lit(ctx.tenant)} and search_key = 'V11FORBIDDEN'`) === 0, 'no payer row');
    s.dbCheck(/true/i.test(String(scalar(`select value from app.tenant_settings where tenant_id = ${lit(ctx.tenant)} and key = 'reviewBeforeVerify'`))), 'setting unchanged');
  });

  await step('N-05', 'Cross-tenant: lab.admin (demo laboratory) cannot read V11 records', async (s) => {
    s.req('lab.admin: GET /patients/{id}, /registration/visits/{id}, /results?accession=…, /billing/visits/{id}/account, list searches');
    s.expect('404 on every direct read; searches return no V11 row');
    const signIn = await lab.signIn('lab.admin', creds.lab);
    s.status(signIn, 200, 'sign-in');
    s.check(lab.profile?.tenantId === DEMO_TENANT, 'lab.admin is in the demo laboratory', lab.profile?.tenantId);
    if (!lab.profile?.branchId) {
      const selected = await lab.post('/api/v1/auth/select-branch', { branchId: DEMO_MAIN_BRANCH });
      s.status(selected, 200, 'select-branch');
    }
    // Ids are global; accession and patient numbers are per-tenant counters (the demo laboratory has its own
    // ACC-2026-00001), so leakage is judged on the V11 ids and the unique V11 patient name, never on the numbers.
    const leaks = (text) => [ctx.patient, ctx.visit, ctx.resultK, ctx.resultL, ctx.testK, ctx.tenant, 'V11 Mona Validation'].filter((needle) => needle && text.includes(needle));
    for (const [route, label] of [
      [`/api/v1/patients/${ctx.patient}`, 'patient'],
      [`/api/v1/registration/visits/${ctx.visit}`, 'visit'],
      [`/api/v1/billing/visits/${ctx.visit}/account`, 'account'],
      [`/api/v1/results/${ctx.resultK}/log`, 'result log'],
      [`/api/v1/catalogue/tests/${ctx.testK}`, 'test'],
    ]) {
      const response = await lab.get(route);
      s.status(response, 404, label);
      s.check(leaks(response.text).length === 0, `${label}: no V11 data in the body`, leaks(response.text));
    }
    const sameNumber = await lab.get(`/api/v1/results?accession=${encodeURIComponent(ctx.accession)}`);
    s.saw(`results?accession=${ctx.accession} ${sameNumber.status} (visit ${sameNumber.json?.visit?.visitId === ctx.visit ? 'V11 — LEAK' : 'of the demo laboratory with the same per-tenant number'})`);
    s.check(leaks(sameNumber.text).length === 0, 'results by the same accession number show only the demo laboratory\'s own visit', leaks(sameNumber.text));
    const search = await lab.get(`/api/v1/patients?search=${encodeURIComponent('V11 Mona Validation')}`);
    s.status(search, 200, 'patient search');
    s.check(!(search.json?.items ?? []).some((item) => item.id === ctx.patient) && leaks(search.text).length === 0, 'patient search returns no V11 row', search.json?.items?.map((item) => item.fullName));
    const visits = await lab.get(`/api/v1/registration/visits?accession=${encodeURIComponent(ctx.accession)}`);
    s.check(visits.status === 200 && !(visits.json?.items ?? []).some((item) => item.id === ctx.visit) && leaks(visits.text).length === 0, 'visit search by the accession number returns no V11 visit', `${visits.status} ${visits.json?.items?.length}`);
    const payments = await lab.get(`/api/v1/billing/payments?accession=${encodeURIComponent(ctx.accession)}`);
    s.check(payments.status === 200 && leaks(payments.text).length === 0 && !(payments.json?.items ?? []).some((item) => item.visitId === ctx.visit), 'payment register shows no V11 payment', `${payments.status} ${payments.json?.items?.length}`);
    const audit = await lab.get(`/api/v1/audit/entries?entityId=${ctx.patient}`);
    s.check(audit.status === 200 && (audit.json?.items ?? []).length === 0, 'audit entries of the V11 patient: none for lab.admin', `${audit.status} ${audit.json?.items?.length}`);
    s.dbCheck(rows(`select 1 from audit.field_changes where tenant_id = ${lit(DEMO_TENANT)} and correlation_id like ${lit(`v11-${RUN}-%`)}`).length === 0, 'no audit row written in the demo tenant by these reads');
  });

  await step('N-06', 'RLS probe as lis_app: another tenant set → 0 V11 rows; own tenant → rows visible (control)', async (s) => {
    s.req("psql -U lis_app: BEGIN READ ONLY; set_config('app.current_tenant', <tenant>, false); select count(*) …; ROLLBACK");
    s.expect('demo tenant: 0 patients/visits/results/payments/audit rows of V11; V11 tenant: 1/1/2/2/>0');
    const probe = (tenant) => {
      const out = psql('app', [
        'begin transaction read only',
        `select set_config('app.bypass_rls', 'off', false), set_config('app.current_tenant', ${lit(tenant)}, false)`,
        `select json_build_object('patients', (select count(*) from app.patients where id = ${lit(ctx.patient)}), 'visits', (select count(*) from app.visits where id = ${lit(ctx.visit)}), 'results', (select count(*) from app.results where visit_id = ${lit(ctx.visit)}), 'payments', (select count(*) from app.payments where visit_id = ${lit(ctx.visit)}), 'audit', (select count(*) from audit.field_changes where tenant_id = ${lit(ctx.tenant)}), 'anyV11', (select count(*) from app.patients where tenant_id = ${lit(ctx.tenant)}))`,
        'rollback',
      ]);
      const line = out.trim().split(/\r?\n/).filter((text) => text.startsWith('{')).pop();
      return JSON.parse(line);
    };
    const other = probe(DEMO_TENANT);
    s.saw(`demo tenant: ${JSON.stringify(other)}`);
    s.dbCheck(Object.values(other).every((count) => count === 0), 'other tenant sees 0 V11 rows', other);
    const own = probe(ctx.tenant);
    s.saw(`own tenant: ${JSON.stringify(own)}`);
    s.dbCheck(own.patients === 1 && own.visits === 1 && own.results === 2 && own.payments === 2 && own.audit > 0, 'own tenant sees its rows (probe is meaningful)', own);
    const none = (() => {
      const out = psql('app', ['begin transaction read only', `select set_config('app.bypass_rls', 'off', false), set_config('app.current_tenant', '', false)`, `select count(*) from app.patients where id = ${lit(ctx.patient)}`, 'rollback']);
      return Number(out.trim().split(/\r?\n/).filter((text) => /^\d+$/.test(text)).pop());
    })();
    s.dbCheck(none === 0, 'no tenant set → 0 rows', none);
  });

  await step('N-07', 'Audit API: entries of the tenant and hash-chain verification', async (s) => {
    s.req('GET /api/v1/audit/entries?entityType=Patient&entityId=…; GET /api/v1/audit/verify-chain');
    s.expect('200 entries (Patient Created + Modified) ; 200 {ok:true, checked = rows of the tenant}');
    const entries = await admin.get(`/api/v1/audit/entries?entityId=${ctx.patient}&pageSize=50`);
    s.status(entries, 200, 'entries');
    const items = entries.json?.items ?? [];
    s.check(items.some((item) => item.kind === 'FieldChange' && item.action === 'Created') && items.some((item) => item.kind === 'FieldChange' && item.action === 'Modified'), 'Created + Modified field changes listed', items.map((item) => `${item.kind}:${item.action}`));
    const chain = await admin.get('/api/v1/audit/verify-chain');
    s.status(chain, 200, 'verify-chain');
    const count = scalar(`select count(*) as n from audit.audit_trail where tenant_id = ${lit(ctx.tenant)}`);
    s.check(chain.json?.ok === true && chain.json?.firstBrokenSequence === null, 'chain ok', chain.json);
    s.dbCheck(chain.json?.checked === count, `checked = ${count} audit_trail rows of the tenant`, chain.json?.checked);
    const seq = rows(`select sequence from audit.audit_trail where tenant_id = ${lit(ctx.tenant)} order by sequence`).map((row) => Number(row.sequence));
    s.dbCheck(seq.every((value, index) => value === index + 1), 'per-tenant sequence is gap-free 1…n', seq.slice(0, 5));
    const linked = scalar(`select count(*) as n from audit.audit_trail a join audit.audit_trail b on b.tenant_id = a.tenant_id and b.sequence = a.sequence - 1 where a.tenant_id = ${lit(ctx.tenant)} and a.prev_hash = b.entry_hash`);
    s.dbCheck(linked === count - 1, 'every row\'s prev_hash = previous row\'s entry_hash', `${linked}/${count - 1}`);
    const forbidden = await tech.get('/api/v1/audit/entries');
    s.status(forbidden, 403, 'tech without audit.view');
  });

  await step('N-08', 'Append-only audit tables (definitions read, no write attempted)', async (s) => {
    s.req('pg_trigger / pg_proc / has_table_privilege');
    s.expect('BEFORE UPDATE OR DELETE trigger → audit.refuse_change() and BEFORE TRUNCATE trigger → audit.refuse_truncate(), both raising and enabled, on the three tables; lis_app has INSERT only');
    for (const table of ['audit.audit_trail', 'audit.field_changes', 'audit.security_events']) {
      // Since migration AppendOnlyTruncateGuard (validation fix D-01) each append-only table carries two triggers: the row
      // trigger refusing UPDATE/DELETE and a statement trigger refusing TRUNCATE.
      const triggers = rows(`select tgname, tgenabled, pg_get_triggerdef(t.oid) as def from pg_trigger t where tgrelid = ${lit(table)}::regclass and not tgisinternal order by tgname`);
      const rowTrigger = triggers.find((t) => /BEFORE DELETE OR UPDATE/.test(t.def)) ?? null;
      s.dbCheck(rowTrigger !== null && /audit\.refuse_change\(\)/.test(rowTrigger.def) && rowTrigger.tgenabled === 'O', `${table}: ${rowTrigger?.tgname} BEFORE DELETE OR UPDATE, enabled`, triggers);
      const truncateTrigger = triggers.find((t) => /BEFORE TRUNCATE/.test(t.def)) ?? null;
      s.dbCheck(truncateTrigger !== null && /refuse_truncate\(\)/.test(truncateTrigger.def) && truncateTrigger.tgenabled === 'O', `${table}: ${truncateTrigger?.tgname} BEFORE TRUNCATE, enabled`, triggers);
      const grants = one(`select has_table_privilege('lis_app', ${lit(table)}, 'INSERT') as ins, has_table_privilege('lis_app', ${lit(table)}, 'UPDATE') as upd, has_table_privilege('lis_app', ${lit(table)}, 'DELETE') as del, has_table_privilege('lis_app', ${lit(table)}, 'TRUNCATE') as trunc`);
      s.dbCheck(grants?.ins === true && grants?.upd === false && grants?.del === false && grants?.trunc === false, `${table}: lis_app INSERT only`, grants);
    }
    const body = scalar(`select prosrc from pg_proc where oid = 'audit.refuse_change'::regproc`);
    s.dbCheck(/RAISE EXCEPTION/i.test(body ?? ''), 'refuse_change() raises an exception', body);
    const truncateBody = scalar(`select prosrc from pg_proc where oid = 'audit.refuse_truncate'::regproc`);
    s.dbCheck(/RAISE EXCEPTION/i.test(truncateBody ?? ''), 'refuse_truncate() raises an exception', truncateBody);
  });

  await step('N-09', 'Tenant tables without row-level security (inventory)', async (s) => {
    s.req('pg_class/pg_policies over every table with a tenant_id column');
    s.expect('every app.* / audit.* tenant table: RLS enabled + forced + a policy');
    const list = rows(`select n.nspname || '.' || c.relname as t, c.relrowsecurity as rls, c.relforcerowsecurity as forced, (select count(*) from pg_policies p where p.schemaname = n.nspname and p.tablename = c.relname) as policies from pg_class c join pg_namespace n on n.oid = c.relnamespace where c.relkind = 'r' and n.nspname in ('app','audit','identity','ops','saas') and exists (select 1 from pg_attribute a where a.attrelid = c.oid and a.attname = 'tenant_id' and not a.attisdropped) order by 1`);
    const weak = list.filter((row) => !row.rls || !row.forced || row.policies === 0);
    const appWeak = weak.filter((row) => /^(app|audit)\./.test(row.t));
    s.dbCheck(appWeak.length === 0, `${list.length} tenant tables; app/audit tables without forced RLS: ${appWeak.length}`, appWeak.map((row) => row.t));
    s.saw(`no RLS (outside app/audit, by design per D-06 / platform schema): ${weak.filter((row) => !/^(app|audit)\./.test(row.t)).map((row) => row.t).join(', ')}`);
  });

  // ================================================================== 4. sign-out (cookie endpoint) and state
  await step('S-01', 'Sign-out of the four sessions through the cookie endpoint', async (s) => {
    s.req(`POST /api/v1/auth/sign-out (cookie, Origin ${api.origin}) ×4`);
    s.expect('204 each; refresh sessions revoked; the old access token is refused');
    for (const session of [admin, tech, lab, platform]) {
      if (!session.cookie) continue;
      const response = await session.cookiePost('/api/v1/auth/sign-out');
      s.status(response, 204, session.label);
    }
    const live = rows(`select id from identity.refresh_sessions where platform_user_id in (${[ctx.adminUserId, ctx.techUserId].filter(Boolean).map(lit).join(',')}) and not is_revoked and rotated_at_utc is null`);
    s.dbCheck(live.length === 0, 'no live refresh session for the V11 users', live.length);
    const after = await admin.get('/api/v1/auth/me');
    s.status(after, 401, 'me with the revoked session');
  });

  saveState({
    run: RUN, base: BASE, tenant: ctx.tenant, clientName: ctx.clientName, adminUsername: ctx.adminUsername, adminPassword: ctx.adminPassword,
    techUsername: ctx.techUsername, mainBranch: ctx.mainBranch, patient: ctx.patient, patientNumber: ctx.patientNumber, visit: ctx.visit, accession: ctx.accession,
    payments: [ctx.payment1, ctx.payment2], refund: ctx.refund, uniqueObserved: ctx.uniqueObserved,
  });
}

// ---- the deployed path (read-mostly) --------------------------------------------------------------------------------

async function deployedRun(api) {
  const state = JSON.parse(fs.readFileSync(STATE_FILE, 'utf8'));
  ctx.tenant = state.tenant;
  const admin = new Session(api, state.adminUsername);

  await step('D-01', `Sign-in on the installed site as ${state.adminUsername}`, async (s) => {
    s.req(`POST ${api.base}/api/v1/auth/sign-in (sealed; audience = POST ${api.pathOf('/api/v1/auth/sign-in')})`);
    s.expect('200 signed-in in the V11 tenant; __Secure-rt cookie (Secure, HttpOnly, SameSite=Strict, Path=/api/v1/auth)');
    const response = await admin.signIn(state.adminUsername, state.adminPassword);
    s.status(response, 200);
    s.check(admin.profile?.tenantId === state.tenant, 'tenant = V11 tenant', admin.profile?.tenantId);
    const cookie = admin.lastSetCookie ?? '';
    s.saw(`cookie: ${cookie.split('=')[0]}; ${cookie.split(';').slice(1).map((part) => part.trim().split('=')[0]).join(', ')}`);
    s.check(/^__Secure-rt=/.test(cookie) && /;\s*Secure/i.test(cookie) && /HttpOnly/i.test(cookie) && /SameSite=Strict/i.test(cookie), 'production cookie attributes', cookie.replace(/=[^;]+/, '=…'));
    if (!admin.profile?.branchId) {
      const selected = await admin.post('/api/v1/auth/select-branch', { branchId: state.mainBranch });
      s.status(selected, 200, 'select-branch');
    }
    const session = one(`select tenant_id, branch_id, revoked_at_utc from identity.refresh_sessions where platform_user_id = (select id from identity.platform_users where username = ${lit(state.adminUsername)}) order by created_at_utc desc limit 1`);
    s.dbCheck(session?.tenant_id === state.tenant && session?.revoked_at_utc === null, 'refresh session written to the same database (lis @ 5433)', session);
  });

  await step('D-02', 'List: patients of the V11 tenant through the installed site', async (s) => {
    s.req('GET /api/v1/patients?search=V11');
    s.expect('200 with the patient created by the full run');
    const response = await admin.get('/api/v1/patients?search=V11');
    s.status(response, 200);
    s.check((response.json?.items ?? []).some((item) => item.id === state.patient), 'the V11 patient is listed', response.json?.items?.map((item) => item.number));
    const headers = response.headers;
    s.check(headers['strict-transport-security'] !== undefined && headers['x-content-type-options'] === 'nosniff', 'security headers present (HSTS, nosniff)', headers);
  });

  await step('D-03', 'Create one harmless V11 record (unit) and check it in the database', async (s) => {
    const name = `V11-dep-${RUN}`;
    s.req(`POST /api/v1/catalogue/units {${name}}`);
    s.expect('201; app.measurement_units row in the V11 tenant; audit row with the correlation id');
    const response = await admin.post('/api/v1/catalogue/units', { name });
    s.status(response, 201);
    const row = one(`select * from app.measurement_units where id = ${lit(response.json?.id)}`);
    s.dbCheck(row?.tenant_id === state.tenant && row?.name === name && row?.is_active, 'measurement_units row in the V11 tenant', row);
    await verifyAuditAndOutbox(s, response.correlation, state.tenant, { fieldChanges: ['MeasurementUnit:Created'], events: ['MeasurementUnitCreated'] });
  });

  await step('D-04', 'Platform fence on the installed site: the V11 administrator cannot reach /platform/clients', async (s) => {
    s.req('GET /api/v1/platform/clients as the laboratory administrator');
    s.expect('403');
    const response = await admin.get('/api/v1/platform/clients');
    s.status(response, 403);
  });

  await step('D-05', 'Sign-out on the installed site (cookie endpoint, Origin of the site)', async (s) => {
    s.req(`POST /api/v1/auth/sign-out (Origin ${api.origin}, Sec-Fetch-Site same-origin)`);
    s.expect('204; session revoked');
    const response = await admin.cookiePost('/api/v1/auth/sign-out');
    s.status(response, 204);
    const after = await admin.get('/api/v1/auth/me');
    s.status(after, 401, 'me after sign-out');
  });
}

// ---- smoke (connectivity: envelope, sign-in, me, sign-out; writes nothing but the session) ----------------------------

async function smokeRun(api) {
  const platform = new Session(api, 'platform.admin');
  await step('S-00', 'Smoke: platform.admin sign-in, me, sign-out', async (s) => {
    const response = await platform.signIn('platform.admin', platformCredentials().platform);
    s.status(response, 200, 'sign-in');
    s.status(await platform.get('/api/v1/auth/me'), 200, 'me');
    s.status(await platform.cookiePost('/api/v1/auth/sign-out'), 204, 'sign-out');
  });
}

// ---- main -----------------------------------------------------------------------------------------------------------

function printTable() {
  const passed = steps.filter((record) => record.verdict === 'PASS').length;
  console.log('\n| Step | Title | Verdict | Observed | DB checks |');
  console.log('| ---- | ----- | ------- | -------- | --------- |');
  for (const record of steps) {
    const dbOk = record.db.filter((line) => line.startsWith('ok')).length;
    const dbFail = record.db.filter((line) => line.startsWith('FAIL')).length;
    console.log(`| ${record.id} | ${record.title} | ${record.verdict} | ${record.observed.join('; ')} | ${dbOk} ok${dbFail ? `, ${dbFail} FAIL` : ''} |`);
  }
  console.log(`\n${passed}/${steps.length} steps passed (run ${RUN}, ${BASE}${DEPLOYED ? ', deployed path' : ''}). Details: ${path.relative(ROOT, JSON_OUT)}`);
  return passed === steps.length;
}

async function main() {
  const api = new Api(BASE, { insecureTls: INSECURE_TLS });
  console.log(`Sky LIS backend cycle — run ${RUN} against ${BASE}${DEPLOYED ? ' (deployed path)' : ''}\n`);
  const health = await api.request('GET', '/health/live').catch((error) => ({ status: 0, text: error.message }));
  if (health.status !== 200) {
    console.error(`API not reachable at ${BASE}: ${health.status} ${health.text}`);
    process.exit(1);
  }
  if (SMOKE) await smokeRun(api);
  else if (DEPLOYED) await deployedRun(api);
  else await fullCycle(api);
  fs.mkdirSync(path.dirname(JSON_OUT), { recursive: true });
  fs.writeFileSync(JSON_OUT, JSON.stringify({ run: RUN, base: BASE, deployed: DEPLOYED, ctx: { ...ctx, adminPassword: undefined, techPassword: undefined }, steps }, null, 2), 'utf8');
  const ok = printTable();
  process.exit(ok ? 0 : 1);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
