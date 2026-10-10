#!/usr/bin/env node
// Cuts a release: every push to GitHub is a versioned release recorded in README.md (README "Versioning and releases").
//   node scripts/release.mjs <patch|minor|major|X.Y.Z> [--dry-run] [--trailer "Co-Authored-By: Name <email>"]...
// 1. reads the bullets under "### Unreleased" in README.md (refuses when there are none),
// 2. bumps VERSION and the frontend package (package.json + package-lock.json),
// 3. moves the bullets under "### vX.Y.Z — <date>", refreshes the version line and leaves an empty Unreleased section,
// 4. commits only those files ("Release vX.Y.Z") and creates the annotated tag vX.Y.Z on that commit.
// Commit the code changes first; then run this, then `git push` (push.followTags sends the tag; the pre-push hook checks it).
import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const files = {
  version: join(root, 'VERSION'),
  readme: join(root, 'README.md'),
  pkg: join(root, 'frontend', 'package.json'),
  lock: join(root, 'frontend', 'package-lock.json'),
};
const releaseFiles = ['VERSION', 'README.md', 'frontend/package.json', 'frontend/package-lock.json'];
const semver = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
const placeholder = '_Nothing yet._';

const args = process.argv.slice(2);
const dryRun = args.includes('--dry-run');
const trailers = [];
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--trailer' && args[i + 1]) trailers.push(args[++i]);
}
const bump = args.find((a, i) => !a.startsWith('--') && args[i - 1] !== '--trailer');
if (!bump) fail('usage: node scripts/release.mjs <patch|minor|major|X.Y.Z> [--dry-run] [--trailer "Key: value"]');

const git = (...a) => execFileSync('git', a, { cwd: root, encoding: 'utf8' }).trim();

const current = readFileSync(files.version, 'utf8').trim();
const match = semver.exec(current);
if (!match) fail(`VERSION holds "${current}", which is not MAJOR.MINOR.PATCH`);
const [maj, min, pat] = match.slice(1).map(Number);
const next = bump === 'major' ? `${maj + 1}.0.0`
  : bump === 'minor' ? `${maj}.${min + 1}.0`
  : bump === 'patch' ? `${maj}.${min}.${pat + 1}`
  : bump;
if (!semver.test(next)) fail(`"${bump}" is not patch, minor, major or MAJOR.MINOR.PATCH`);
if (compare(next, current) <= 0) fail(`${next} is not greater than the current version ${current}`);
const tag = `v${next}`;
if (git('tag', '--list', tag)) fail(`tag ${tag} already exists`);

const readme = readFileSync(files.readme, 'utf8').replace(/\r\n/g, '\n');
const unreleased = /### Unreleased\n([\s\S]*?)(?=\n### |\n## |$)/.exec(readme);
if (!unreleased) fail('README.md has no "### Unreleased" section under "## Release history"');
const notes = unreleased[1].trim();
if (!notes || notes === placeholder || !/^- /m.test(notes)) {
  fail('README.md "### Unreleased" lists no changes. Describe every change of this release there first (one "- " bullet each).');
}

const date = new Date().toISOString().slice(0, 10);
// Function replacements: release notes may contain "$" sequences that a replacement string would expand.
const updatedReadme = readme
  .replace(unreleased[0], () => `### Unreleased\n\n${placeholder}\n\n### ${tag} — ${date}\n\n${notes}\n`)
  .replace(/<!-- version:start -->[\s\S]*?<!-- version:end -->/, () => `<!-- version:start -->**Version ${next}** · released ${date} · [release history](#release-history)<!-- version:end -->`);
if (!updatedReadme.includes(`**Version ${next}**`)) fail('README.md lost its <!-- version:start --> … <!-- version:end --> line');

const pkg = JSON.parse(readFileSync(files.pkg, 'utf8'));
pkg.version = next;
const lock = JSON.parse(readFileSync(files.lock, 'utf8'));
lock.version = next;
if (lock.packages?.['']) lock.packages[''].version = next;

console.log(`Release ${current} → ${next} (${date})\n\n${notes}\n`);
if (dryRun) {
  console.log('--dry-run: nothing written');
  process.exit(0);
}

writeFileSync(files.version, `${next}\n`);
writeFileSync(files.readme, updatedReadme);
writeFileSync(files.pkg, `${JSON.stringify(pkg, null, 2)}\n`);
writeFileSync(files.lock, `${JSON.stringify(lock, null, 2)}\n`);

// The release process is local git configuration; make sure this clone enforces it.
git('config', 'core.hooksPath', '.githooks');
git('config', 'push.followTags', 'true');

git('add', '--', ...releaseFiles);
const message = [`Release ${tag}`, '', notes, ...(trailers.length ? ['', ...trailers] : [])].join('\n');
execFileSync('git', ['commit', '--quiet', '-F', '-', '--', ...releaseFiles], { cwd: root, input: message, stdio: ['pipe', 'inherit', 'inherit'] });
execFileSync('git', ['tag', '-a', tag, '-F', '-'], { cwd: root, input: `Sky LIS ${tag}\n\n${notes}\n` });
console.log(`Committed and tagged ${tag}. Push with: git push`);

function compare(a, b) {
  const pa = a.split('.').map(Number);
  const pb = b.split('.').map(Number);
  for (let i = 0; i < 3; i++) if (pa[i] !== pb[i]) return pa[i] - pb[i];
  return 0;
}

function fail(message) {
  console.error(`release: ${message}`);
  process.exit(1);
}
