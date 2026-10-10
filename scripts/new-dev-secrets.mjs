#!/usr/bin/env node
// Generates the development secrets of 02 §6.4 and stores them in the WebApi user-secrets store (never in files).
//   node scripts/new-dev-secrets.mjs            # prints nothing secret; run from the repository root
// All keys live under the Lis root (CLAUDE.md §1): Lis:Jwt:SigningKey (P-256 private key PEM),
// Lis:Jwt:SubjectEncryptionKey (32 bytes), Lis:DataProtection:MasterKey (32 bytes),
// Lis:Database:ConnectionString (password read from deploy/postgres/.env when present).
import { generateKeyPairSync, randomBytes } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, readFileSync } from 'node:fs';

const project = 'src/Platform.WebApi/Platform.WebApi.csproj';
const { privateKey } = generateKeyPairSync('ec', { namedCurve: 'P-256' });

const secrets = {
  'Lis:Jwt:SigningKey': privateKey.export({ type: 'pkcs8', format: 'pem' }),
  'Lis:Jwt:SubjectEncryptionKey': randomBytes(32).toString('base64'),
  'Lis:DataProtection:MasterKey': randomBytes(32).toString('base64'),
};

if (existsSync('deploy/postgres/.env')) {
  const env = Object.fromEntries(readFileSync('deploy/postgres/.env', 'utf8').split(/\r?\n/).filter((l) => l.includes('=')).map((l) => l.split('=')));
  if (env.LIS_APP_PASSWORD) {
    secrets['Lis:Database:ConnectionString'] = `Host=127.0.0.1;Port=5433;Database=lis;Username=lis_app;Password=${env.LIS_APP_PASSWORD}`;
  }
}

// Generic keys from an earlier layout are removed so nothing outside the Lis root lingers in the store.
for (const stale of ['ConnectionStrings:Postgres', 'Jwt:SigningKey', 'Jwt:SubjectEncryptionKey', 'DataProtection:MasterKey']) {
  try { execFileSync('dotnet', ['user-secrets', 'remove', stale, '--project', project], { stdio: 'ignore' }); } catch { /* not present */ }
}

for (const [key, value] of Object.entries(secrets)) {
  execFileSync('dotnet', ['user-secrets', 'set', key, value, '--project', project], { stdio: ['ignore', 'ignore', 'inherit'] });
  console.log(`set ${key}`);
}
console.log('done: secrets stored in the user-secrets store of Platform.WebApi');
