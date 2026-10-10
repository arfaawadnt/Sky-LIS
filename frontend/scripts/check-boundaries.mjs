#!/usr/bin/env node
// Module boundaries of 01 §6.2: core/ never imports features/; features never import each other; shared/ui never
// injects a facade (no features import). Exit code 1 on the first violation; run as "npm run lint:boundaries".
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative, resolve, sep } from 'node:path';

const root = resolve('src/app');
const violations = [];

function walk(dir) {
  for (const entry of readdirSync(dir)) {
    const path = join(dir, entry);
    if (statSync(path).isDirectory()) walk(path);
    else if (path.endsWith('.ts')) check(path);
  }
}

function check(path) {
  const rel = relative(root, path).split(sep).join('/');
  const area = rel.startsWith('core/') ? 'core' : rel.startsWith('shared/') ? 'shared' : rel.startsWith('features/') ? 'features' : 'app';
  const feature = area === 'features' ? rel.split('/')[1] : null;
  const source = readFileSync(path, 'utf8');
  for (const match of source.matchAll(/from\s+['"]([^'"]+)['"]/g)) {
    const spec = match[1];
    if (!spec.startsWith('.')) continue;
    const target = relative(root, resolve(path, '..', spec)).split(sep).join('/');
    if ((area === 'core' || area === 'shared') && target.startsWith('features/')) violations.push(`${rel} → ${target} (${area} must not import features)`);
    if (area === 'features' && target.startsWith('features/') && target.split('/')[1] !== feature) violations.push(`${rel} → ${target} (features must not import each other)`);
  }
}

walk(root);
if (violations.length) { console.error(violations.join('\n')); process.exit(1); }
console.log('boundaries ok');
