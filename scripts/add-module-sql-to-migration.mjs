#!/usr/bin/env node
/**
 * Appends every module SQL file (src/Platform.Infrastructure/Persistence/<Module>/<Module>.migration.sql) that no
 * migration has consumed yet to the Up() of the given migration, as `migrationBuilder.Sql(...)` blocks tagged with a
 * `// module-sql: <relative path>` comment. A file is consumed once: later waves append only new files. Run after
 * scripts/add-rls-to-migration.mjs so the policies come first.
 *
 *   node scripts/add-module-sql-to-migration.mjs src/Platform.Infrastructure/Persistence/Migrations/<timestamp>_<Name>.cs
 */
import { readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { join, relative, sep } from 'node:path';

const file = process.argv[2];
if (!file) {
  console.error('usage: add-module-sql-to-migration.mjs <migration.cs>');
  process.exit(2);
}

const root = process.cwd();
const persistence = join(root, 'src', 'Platform.Infrastructure', 'Persistence');
const migrationsDir = join(persistence, 'Migrations');

const walk = (dir) =>
  readdirSync(dir).flatMap((name) => {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) {
      return name === 'Migrations' ? [] : walk(full);
    }
    return name.endsWith('.migration.sql') ? [full] : [];
  });

const consumed = new Set();
for (const name of readdirSync(migrationsDir).filter((n) => n.endsWith('.cs'))) {
  const text = readFileSync(join(migrationsDir, name), 'utf8');
  for (const match of text.matchAll(/\/\/ module-sql: (\S+)/g)) {
    consumed.add(match[1]);
  }
}

const pending = walk(persistence)
  .map((full) => ({ full, rel: relative(root, full).split(sep).join('/') }))
  .filter(({ rel }) => !consumed.has(rel))
  .sort((a, b) => a.rel.localeCompare(b.rel));

if (pending.length === 0) {
  console.log('no new module SQL files');
  process.exit(0);
}

const raw = readFileSync(file, 'utf8');
const crlf = raw.includes('\r\n');
let source = raw.replace(/\r\n/g, '\n');
const anchor = '        }\n\n        /// <inheritdoc />\n        protected override void Down(';
if (!source.includes(anchor)) {
  console.error('anchor before Down() not found');
  process.exit(1);
}

const blocks = pending
  .map(({ full, rel }) => {
    const sql = readFileSync(full, 'utf8').replace(/\r\n/g, '\n').replace(/"""/g, '""""').trimEnd();
    const indented = sql
      .split('\n')
      .map((line) => (line.length === 0 ? '' : '                ' + line))
      .join('\n');
    return `
            // module-sql: ${rel}
            migrationBuilder.Sql("""
${indented}
                """);`;
  })
  .join('\n');

source = source.replace(anchor, () => blocks + '\n' + anchor);   // a function: $$ in the SQL must stay literal
writeFileSync(file, crlf ? source.replace(/\n/g, '\r\n') : source);
console.log('appended: ' + pending.map((p) => p.rel).join(', '));
