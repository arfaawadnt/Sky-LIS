#!/usr/bin/env node
// Pre-push gate (.githooks/pre-push): every pushed branch tip must be a release (README "Versioning and releases").
//   node scripts/check-release.mjs <remote> <url>   with git's "<local ref> <local sha> <remote ref> <remote sha>" lines on stdin
// For each branch update: VERSION at the tip is MAJOR.MINOR.PATCH, the annotated tag v<VERSION> points at the tip,
// README.md at the tip has the "### v<VERSION>" entry, the version is greater than the one already on the remote, and the
// tag reaches the remote in this push or is already there. Deletions pass; tag pushes must be named vMAJOR.MINOR.PATCH.
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';

const [remote = 'origin'] = process.argv.slice(2);
const zero = /^0+$/;
const semver = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
const git = (...a) => {
  try {
    return execFileSync('git', a, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'ignore'] }).trim();
  } catch {
    return null;
  }
};

const updates = readFileSync(0, 'utf8').split('\n').map(l => l.trim()).filter(Boolean)
  .map(l => { const [localRef, localSha, remoteRef, remoteSha] = l.split(/\s+/); return { localRef, localSha, remoteRef, remoteSha }; });
const pushedTags = new Set(updates.filter(u => u.remoteRef.startsWith('refs/tags/') && !zero.test(u.localSha)).map(u => u.remoteRef));
const errors = [];

for (const u of updates) {
  if (zero.test(u.localSha)) continue;
  if (u.remoteRef.startsWith('refs/tags/')) {
    const name = u.remoteRef.slice('refs/tags/'.length);
    if (!/^v\d+\.\d+\.\d+$/.test(name)) errors.push(`tag ${name}: release tags are named vMAJOR.MINOR.PATCH`);
    continue;
  }
  if (!u.remoteRef.startsWith('refs/heads/')) continue;

  const branch = u.remoteRef.slice('refs/heads/'.length);
  const version = git('show', `${u.localSha}:VERSION`);
  if (!version || !semver.test(version)) {
    errors.push(`${branch}: VERSION is missing or not MAJOR.MINOR.PATCH at ${u.localSha.slice(0, 7)}`);
    continue;
  }
  const tag = `v${version}`;
  const tagged = git('rev-parse', '--verify', '--quiet', `refs/tags/${tag}^{commit}`);
  if (tagged !== u.localSha) {
    errors.push(`${branch}: the pushed commit ${u.localSha.slice(0, 7)} is not the release ${tag}. ` +
      'Describe the changes under "### Unreleased" in README.md, then run: node scripts/release.mjs <patch|minor|major>');
    continue;
  }
  if (git('cat-file', '-t', `refs/tags/${tag}`) !== 'tag') errors.push(`${branch}: ${tag} must be an annotated tag (git tag -a)`);
  const readme = git('show', `${u.localSha}:README.md`) ?? '';
  if (!readme.includes(`### ${tag} `)) errors.push(`${branch}: README.md has no "### ${tag}" release entry`);

  if (!zero.test(u.remoteSha) && git('cat-file', '-e', `${u.remoteSha}^{commit}`) !== null) {
    const previous = git('show', `${u.remoteSha}:VERSION`) ?? '0.0.0';
    if (semver.test(previous) && compare(version, previous) <= 0) {
      errors.push(`${branch}: version ${version} is not greater than ${previous}, already on ${remote}`);
    }
  }

  if (!pushedTags.has(`refs/tags/${tag}`)) {
    const remoteTag = git('ls-remote', '--tags', remote, `refs/tags/${tag}`);
    if (!remoteTag) errors.push(`${branch}: the tag ${tag} is not part of this push. Push with: git push --follow-tags (or git config push.followTags true)`);
  }
}

if (errors.length) {
  console.error('pre-push: refused, every push must be a versioned release recorded in README.md\n');
  for (const e of errors) console.error(`  - ${e}`);
  console.error('');
  process.exit(1);
}

function compare(a, b) {
  const pa = a.split('.').map(Number);
  const pb = b.split('.').map(Number);
  for (let i = 0; i < 3; i++) if (pa[i] !== pb[i]) return pa[i] - pb[i];
  return 0;
}
