#!/usr/bin/env node
/**
 * Adds ENABLE/FORCE ROW LEVEL SECURITY and the tenant_isolation policy (01 §4.4) for every tenant table a migration
 * creates, right after the last CreateTable of its Up() method (01 §4.2 item 5, pitfall P-04). A tenant table is a
 * CreateTable in the `app`, `read` or `audit` schema whose columns include tenant_id. Idempotent: a migration that
 * already carries a policy for a table is left alone.
 *
 *   node scripts/add-rls-to-migration.mjs src/Platform.Infrastructure/Persistence/Migrations/<timestamp>_<Name>.cs
 */
import { readFileSync, writeFileSync } from 'node:fs';

const file = process.argv[2];
if (!file) {
  console.error('usage: add-rls-to-migration.mjs <migration.cs>');
  process.exit(2);
}

const raw = readFileSync(file, 'utf8');
const crlf = raw.includes('\r\n');
let source = raw.replace(/\r\n/g, '\n');

const upStart = source.indexOf('protected override void Up(');
const downStart = source.indexOf('protected override void Down(');
if (upStart < 0 || downStart < 0) {
  console.error('Up()/Down() not found');
  process.exit(1);
}
const up = source.slice(upStart, downStart);

const tables = [];
const createTable = /migrationBuilder\.CreateTable\(\s*name:\s*"([^"]+)",\s*schema:\s*"([^"]+)",([\s\S]*?)\n\s*\}\);/g;
let match;
while ((match = createTable.exec(up)) !== null) {
  const [, name, schema, body] = match;
  if (['app', 'read', 'audit'].includes(schema) && /tenant_id\s*=\s*table\.Column<Guid>/.test(body) && !up.includes(`CREATE POLICY tenant_isolation ON ${schema}.${name}`)) {
    tables.push({ schema, name });
  }
}

if (tables.length === 0) {
  console.log('no tenant table without a policy in ' + file);
  process.exit(0);
}

const policy = ({ schema, name }) => `                ALTER TABLE ${schema}.${name} ENABLE ROW LEVEL SECURITY;
                ALTER TABLE ${schema}.${name} FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON ${schema}.${name}
                    USING (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    )
                    WITH CHECK (
                        tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
                        OR current_setting('app.bypass_rls', true) = 'on'
                    );`;

const block = `
            // Row-level security for every tenant table of this migration, declared where the tables are created (01 §4.2 item 5, pitfall P-04).
            migrationBuilder.Sql("""
${tables.map(policy).join('\n')}
                """);
        }

        /// <inheritdoc />
        protected override void Down(`;

const anchor = '        }\n\n        /// <inheritdoc />\n        protected override void Down(';
if (!source.includes(anchor)) {
  console.error('anchor before Down() not found');
  process.exit(1);
}
source = source.replace(anchor, () => block);   // a function: never interpret $ patterns
writeFileSync(file, crlf ? source.replace(/\n/g, '\r\n') : source);
console.log(`RLS added for ${tables.map((t) => `${t.schema}.${t.name}`).join(', ')}`);
