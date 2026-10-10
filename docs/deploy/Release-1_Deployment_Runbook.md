# Sky LIS — Release 1 Deployment Runbook (LIS-DEP-001)

Version 0.1 · 2026-10-10 · applies to Sky LIS packages built from `VERSION` ≥ 0.2.2 · Windows Server 2022, IIS, PostgreSQL 17

This runbook installs and upgrades Sky LIS on a Windows server with the scripts in `deploy/windows`. It is written for
the operator who holds the server, the certificate and the database superuser; it assumes nothing about any other
application on the machine and touches only the names reserved for LIS (`CLAUDE.md` §2). Every script supports
`-WhatIf`, which prints the plan and changes nothing; run it first, every time.

## 1. Topology

```
browser ──TLS──▶ IIS site lis-ui (host header, ARR + URL Rewrite)
                   ├─ /api, /health, /.well-known ──▶ http://127.0.0.1:5100  Windows service NTLIS (Kestrel, loopback only)
                   └─ everything else ─────────────▶ C:\inetpub\wwwroot\lis-ui\index.html (Angular SPA)
                                                      NTLIS ──▶ PostgreSQL 17, database lis, runtime role lis_app
                                                      uploaded files: E:\lis-data\files
test lane:       lis-ui-test (http 8081) ──▶ http://127.0.0.1:5180  NTLIS_TEST ──▶ database lis_test
```

The SPA and the API share one origin: the refresh cookie is `__Secure-rt; Path=/api/v1/auth; Secure; HttpOnly;
SameSite=Strict`, and the cookie endpoints check `Origin` against `Lis:Security:AllowedOrigins`. Kestrel refuses any
bind that is not loopback; TLS terminates in IIS. HSTS, `nosniff`, `DENY` framing and `no-referrer` are set by the API
for its responses and by `web.config` for the SPA's.

## 2. Reserved names used by this runbook

| Item | Value |
| --- | --- |
| API service / port | `NTLIS` on `127.0.0.1:5100`; test `NTLIS_TEST` on `127.0.0.1:5180` |
| Service account | virtual account `NT SERVICE\NTLIS` / `NT SERVICE\NTLIS_TEST` (no password) |
| Install root | `C:\lis-app\NTLIS\{app, app.previous, config}`; test `C:\lis-app\NTLIS_TEST\…` |
| Configuration file | `C:\lis-app\NTLIS\config\appsettings.Production.json` (secrets inside; ACL: SYSTEM, Administrators, the service account) |
| IIS site / pool / path | `lis-ui` on `C:\inetpub\wwwroot\lis-ui`; test `lis-ui-test` (port 8081) on `C:\inetpub\wwwroot\lis-ui-test` |
| Database / roles | `lis`, owner `lis_owner` (migrations, backups), runtime `lis_app`; test `lis_test`; restore drills `lis_drill` |
| Data, backups | `E:\lis-data\files` (uploads), `E:\lis-data\postgres.env` (database credentials for the backup scripts), `E:\lis-backups` |
| Release staging on the server | `E:\lis-data\releases\<version>` (package copy; removable after the installation) |
| Package on the build box | `E:\LIS\publish\<version>\{api, tools, ui, manifest.json}` |

## 3. Prerequisites

### 3.1 Server

- Windows Server 2022 with IIS (`Web-Server`), the IIS Management Console and the `WebAdministration` PowerShell module.
- **URL Rewrite 2.1** and **Application Request Routing 3.0** (IIS downloads). The ARR proxy must be enabled at server
  level (`install-ui-site.ps1 -EnableArrProxy` does it, or `%windir%\system32\inetsrv\appcmd set config
  -section:system.webServer/proxy /enabled:"True" /commit:apphost`). The script also adds `HTTP_X_FORWARDED_PROTO` to the
  server-level allowed server variables; both settings are additive and shared by every site on the server.
- **ASP.NET Core Runtime 9.0.x** (the Hosting Bundle also installs it; the ANCM module is not needed because IIS only
  proxies to the service). `dotnet --list-runtimes` must list `Microsoft.AspNetCore.App 9.x`.
- **PostgreSQL 17** reachable from the server (local instance or network), with the client tools
  (`psql`, `pg_dump`, `pg_restore`; default `C:\Program Files\PostgreSQL\17\bin`).
- A TLS certificate for the portal host name in `Cert:\LocalMachine\My`, and DNS for that host name.
- The service and the web server run with Windows PowerShell 5.1; no Node, npm or .NET SDK is needed on the server.

### 3.2 Build box

- .NET SDK 9.0.3xx (`global.json`), Node 24 and npm 11 (`frontend/.nvmrc`), git, a clone of the repository at the
  release tag.
- Node is also where the secrets are generated (`deploy/SECRETS.md`); never generate them on a shared machine.

### 3.3 Service-aware host

The API process registers with the Service Control Manager: `Program.cs` calls `builder.Host.UseWindowsService()`
(package `Microsoft.Extensions.Hosting.WindowsServices`, since 0.3.0), which also routes the host's logging to the
Application event log when it runs as a service and is a no-op from a console. `install-api-service.ps1` still refuses
a package whose `api` folder lacks `Microsoft.Extensions.Hosting.WindowsServices.dll` (a package built from an older
version), because the SCM would stop such a process after 30 s with error 1053.

## 4. Build the package (build box)

```powershell
cd E:\LIS
git checkout vX.Y.Z
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\publish.ps1 -WhatIf
powershell -NoProfile -ExecutionPolicy Bypass -File deploy\windows\publish.ps1
```

`publish.ps1` reads `VERSION`, publishes the API (`dotnet publish src/Platform.WebApi -c Release -r win-x64
--self-contained false`) and `Platform.DevTools` under a private build slot, runs `npm ci` and `npm run build` in
`frontend/`, and writes `E:\LIS\publish\<version>\{api, tools, ui}` plus `manifest.json` (version, commit, date, tool
versions, file counts). `appsettings.Development.json` is removed from the output and every JSON file is scanned for
password-bearing values; the package holds no secret. Stop the frontend dev server first, or pass `-SkipNpmCi`.

Copy to the server, e.g. to `E:\lis-data\releases\<version>`:

- `E:\LIS\publish\<version>\` (whole folder),
- `deploy\windows\` (scripts, `web.config`, `appsettings.Production.template.json`),
- `deploy\postgres\roles.sql`, `schema.sql`, `migrations.sql`.

## 5. First installation

Run everything from an elevated Windows PowerShell on the server, from the release staging folder.

### 5.1 Database (once per server)

As the PostgreSQL superuser, connected to `postgres`:

```powershell
& "C:\Program Files\PostgreSQL\17\bin\psql.exe" -h <db-host> -p 5432 -U postgres -d postgres -v ON_ERROR_STOP=1 -v owner_pw='<lis_owner password>' -v app_pw='<lis_app password>' -f .\roles.sql
```

`roles.sql` creates `lis_owner` (LOGIN, BYPASSRLS, no superuser, no CREATEDB/CREATEROLE), `lis_app` (LOGIN, NOBYPASSRLS)
and the database `lis` owned by `lis_owner`, and limits `lis_app` to CONNECT and TEMPORARY. Then, as `lis_owner` on `lis`:

```powershell
& "C:\Program Files\PostgreSQL\17\bin\psql.exe" -h <db-host> -p 5432 -U lis_owner -d lis -v ON_ERROR_STOP=1 -f .\schema.sql
& "C:\Program Files\PostgreSQL\17\bin\psql.exe" -h <db-host> -p 5432 -U lis_owner -d lis -v ON_ERROR_STOP=1 -f .\migrations.sql
& "C:\Program Files\PostgreSQL\17\bin\psql.exe" -h <db-host> -p 5432 -U lis_owner -d lis -Atc "select count(*) from ops.__ef_migrations_history"
```

`schema.sql` creates the schemas `identity, app, audit, saas, read, ops` and the default privileges of `lis_app`;
`migrations.sql` is idempotent and applies every migration not yet in `ops.__ef_migrations_history`. The runtime role
owns no table (the start-up guard refuses otherwise), so the migrations must run as `lis_owner`, never as `lis_app`.

### 5.2 Database credentials file for the backup scripts

Create `E:\lis-data\postgres.env` (KEY=VALUE lines) and restrict it to SYSTEM and Administrators:

```
LIS_PG_HOST=<db-host>
LIS_PG_PORT=5432
LIS_PG_DATABASE=lis
LIS_PG_SUPERUSER=postgres
LIS_PG_SUPERUSER_PASSWORD=<superuser password; restore drills only>
LIS_OWNER_PASSWORD=<lis_owner password>
LIS_PG_BIN=C:\Program Files\PostgreSQL\17\bin
```

```powershell
icacls E:\lis-data\postgres.env /inheritance:r /grant:r "SYSTEM:(R)" "BUILTIN\Administrators:(F)"
```

### 5.3 Secrets and configuration

1. Copy `appsettings.Production.template.json` to a protected folder (for example `E:\lis-data\staging\appsettings.Production.json`,
   ACL as above) and replace every `<SET: …>` placeholder:

   | Key | Value |
   | --- | --- |
   | `Lis:Database:ConnectionString` | `Host=<db-host>;Port=5432;Database=lis;Username=lis_app;Password=<lis_app password>;…` |
   | `Lis:Jwt:Issuer` and `Lis:Security:AllowedOrigins[0]` | the public origin, e.g. `https://lis.example.net` (no path, no trailing slash) |
   | `Lis:Jwt:SigningKey` | a P-256 private key in PEM; in JSON the line breaks are written as `\n` |
   | `Lis:Jwt:SigningKeyId` | a short key id, e.g. `2026-10-k1` (changes with every signing-key rotation) |
   | `Lis:Jwt:SubjectEncryptionKey`, `Lis:DataProtection:MasterKey` | two different values of 32 random bytes, base64 |
   | `Lis:Platform:PortalDomain` | the domain under which client portals are addressed (default `skylis.app`) |

   Generation commands are in `deploy/SECRETS.md` (Node one-liners, run on the build box). Without Node, 32 random
   bytes in Windows PowerShell (cryptographic generator, never `Get-Random`):
   `$b = New-Object byte[] 32; (New-Object System.Security.Cryptography.RNGCryptoServiceProvider).GetBytes($b); [Convert]::ToBase64String($b)`.
   The P-256 key needs Node or OpenSSL (`openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt`).
2. Leave `Kestrel:Endpoints:Http:Url` at `http://127.0.0.1:5100` and `Lis:Security:AllowInsecureLocalDevelopment` at `false`.
3. The file is validated by the install script against the same rules as the start-up guards (placeholders, loopback
   bind, `lis*` database and `lis_*` role, no relaxation, no `CredentialEnvelope:Mode` key) before anything is changed.

### 5.4 API service

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\windows\install-api-service.ps1 -PackagePath E:\lis-data\releases\<version>\api -ConfigSource E:\lis-data\staging\appsettings.Production.json -WhatIf
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\windows\install-api-service.ps1 -PackagePath E:\lis-data\releases\<version>\api -ConfigSource E:\lis-data\staging\appsettings.Production.json
Remove-Item E:\lis-data\staging\appsettings.Production.json
```

The script copies the package to `C:\lis-app\NTLIS\app`, installs the configuration as
`C:\lis-app\NTLIS\config\appsettings.Production.json` (and a copy next to the binaries, both readable only by SYSTEM,
Administrators and `NT SERVICE\NTLIS`), creates or updates the service (`delayed-auto`, restart on failure,
`ASPNETCORE_ENVIRONMENT=Production` pinned in the service's own environment block), creates `E:\lis-data\files` with
modify rights for the service account, starts the service and waits for `http://127.0.0.1:5100/health/ready` (200 =
database reachable and no pending migration). On failure it prints where to look and the rollback command.

### 5.5 IIS site

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\windows\install-ui-site.ps1 -PackagePath E:\lis-data\releases\<version>\ui -HostHeader lis.example.net -CertificateThumbprint <thumbprint> -EnableArrProxy -WhatIf
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\windows\install-ui-site.ps1 -PackagePath E:\lis-data\releases\<version>\ui -HostHeader lis.example.net -CertificateThumbprint <thumbprint> -EnableArrProxy
```

The script checks URL Rewrite, ARR, the proxy flag and the allowed server variable, refuses a binding another site
already owns, stages the SPA, writes `web.config` from the template (API port and the CSP hash of the inline script of
`index.html` filled in), creates the pool `lis-ui` (no managed code) and the site with the `https` binding (SNI) and
the certificate, grants `IIS AppPool\lis-ui` read access and starts both. The previous content stays in
`C:\inetpub\wwwroot\lis-ui.previous`.

### 5.6 Smoke test

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\deploy\windows\smoke-test.ps1 -BaseUrl https://lis.example.net -ApiUrl http://127.0.0.1:5100
```

Checks, through the site: `/health/live`, `/health/ready`, `/.well-known/jwks.json` (one P-256 key), `index.html`
(`no-store`, security headers, no `Server` header), a hashed bundle (`immutable`), a deep route (router fallback), an
i18n dictionary, and `/api/v1/auth/me` → 401 (proxied and guarded). Exit code 0 = all passed, 1 = a check failed.

### 5.7 First administrator

`Platform.DevTools.exe` from the package `tools` folder seeds the National Technology administrator of the platform
tenant (user + membership + role "Platform Administrator" with every permission). It connects with
`LIS_DEVTOOLS_CONNECTION`, set for the current process only:

```powershell
$env:LIS_DEVTOOLS_CONNECTION = 'Host=<db-host>;Port=5432;Database=lis;Username=lis_app;Password=<lis_app password>'
& E:\lis-data\releases\<version>\tools\Platform.DevTools.exe seed-platform-admin <username> <one-time password>
Remove-Item Env:\LIS_DEVTOOLS_CONNECTION
```

Use a one-time password and change it at the first sign-in (account → password). The command line is visible in the
process list and the PowerShell history while it runs; clear the history afterwards (`Clear-History`, and the file
`(Get-PSReadlineOption).HistorySavePath`).

### 5.8 Scheduled backup

```powershell
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-NoProfile -ExecutionPolicy Bypass -File C:\lis-app\deploy\backup-database.ps1 -EnvFile E:\lis-data\postgres.env -BackupRoot E:\lis-backups -RetentionDays 30'
$trigger = New-ScheduledTaskTrigger -Daily -At 02:00
Register-ScheduledTask -TaskName 'NTLIS database backup' -Action $action -Trigger $trigger -User 'SYSTEM' -RunLevel Highest
```

Keep a copy of `deploy\windows` on the server (for example `C:\lis-app\deploy`) so the task and the rollbacks have
the scripts at hand; the scripts contain no secret. Replicate `E:\lis-backups` off the machine.

## 6. Upgrade

1. **Build** the package for the new tag on the build box (§4) and copy it to `E:\lis-data\releases\<version>`.
2. **Dry run** both install scripts with `-WhatIf`.
3. **Backup**: `backup-database.ps1 -EnvFile E:\lis-data\postgres.env` → note the dump name.
4. **Stop the API** so no request runs across the schema change: `Stop-Service NTLIS`.
5. **Migrations**: `psql -U lis_owner -d lis -v ON_ERROR_STOP=1 -f migrations.sql` (idempotent; applies only what is missing).
6. **Service**: `install-api-service.ps1 -PackagePath …\api` (no `-ConfigSource` unless the configuration changed; the
   script keeps the current build as `app.previous`, installs, starts and waits for `/health/ready`).
7. **Site**: `install-ui-site.ps1 -PackagePath …\ui -HostHeader … -CertificateThumbprint …` (previous content kept).
8. **Smoke test** (§5.6). Sign in once in a browser.
9. **Rollback**, if needed, in this order:
   - `install-ui-site.ps1 -Rollback` (previous SPA back),
   - `install-api-service.ps1 -Rollback` (previous build back, same configuration),
   - the database only when the new migration cannot stay: `Stop-Service NTLIS`, then
     `restore-database.ps1 -DumpFile E:\lis-backups\lis-<stamp>.dump -TargetDatabase lis -Replace -AllowProductionTarget`,
     then `Start-Service NTLIS`. Migrations are forward-only; a restore discards every change made after the dump.

Configuration-only changes (a rotated key, a new origin) are an "upgrade" with the same package: pass `-ConfigSource`
and the script re-validates, re-installs the file and restarts. Rotation procedures: `deploy/SECRETS.md`.

## 7. Test lane

The same scripts with `-Test` install `NTLIS_TEST` (5180), `lis-ui-test` (http 8081, or https with
`-CertificateThumbprint`) and a separate configuration under `C:\lis-app\NTLIS_TEST\config`. Its
`appsettings.Production.json` must set `Kestrel:Endpoints:Http:Url` to `http://127.0.0.1:5180`, point at the database
`lis_test` and list the test origin in `AllowedOrigins`. Create `lis_test` once, as the superuser:

```sql
CREATE DATABASE lis_test OWNER lis_owner ENCODING 'UTF8' TEMPLATE template0;
REVOKE ALL ON DATABASE lis_test FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE lis_test TO lis_app;
```

then run `schema.sql` and `migrations.sql` on it as `lis_owner`. Over plain http the browser refuses the `__Secure-rt`
cookie: sign-in works for the 15-minute access token but the silent refresh does not; use a certificate for anything
beyond smoke tests.

## 8. Configuration reference (`appsettings.Production.json`)

Every LIS key lives under `Lis`; the host reads no `ConnectionStrings`, `Jwt` or `DataProtection` section and ignores
`ASPNETCORE_URLS` because Kestrel's endpoint is pinned in the file. Durations are `d.hh:mm:ss`.

| Key | Meaning | Guard |
| --- | --- | --- |
| `Kestrel:Endpoints:Http:Url` | loopback endpoint (`http://127.0.0.1:5100`, test 5180) | non-loopback refused at start-up |
| `Logging:*` | levels of the console and the Windows event log providers | — |
| `Lis:Database:ConnectionString` | `lis_app` on database `lis…` | database not `lis*` or role not `lis_*` refused before connecting; SUPERUSER, BYPASSRLS, CREATEDB, CREATEROLE or table ownership refused after |
| `Lis:Jwt:Issuer`, `Audience` | token issuer (the public origin) and audience (`platform-api`) | required |
| `Lis:Jwt:SigningKey`, `SigningKeyId` | ES256 private key (PEM) and key id published in the JWKS | must be a P-256 private key |
| `Lis:Jwt:SubjectEncryptionKey`, `PreviousSubjectEncryptionKeys` | 32-byte keys that encrypt the `sub` claim; previous ones accepted during a rotation | 32 bytes base64 each |
| `Lis:DataProtection:MasterKey` | 32-byte key protecting envelope keys at rest | 32 bytes base64 |
| `Lis:Security:AllowInsecureLocalDevelopment` | loopback development relaxation (`rt` cookie, no HSTS) | must be `false` on a server; `true` writes an error-level security event |
| `Lis:Security:AllowedOrigins` | public origins accepted by the cookie endpoints' origin check | empty = the request's own origin (not reliable behind the proxy; always set it) |
| `Lis:Security:Session:*` | access token 15 min max, browser 8 h sliding / 12 h absolute, machine 30 / 90 days, lockout 5 attempts / 30 min, step-up 5 min, OTP 6 digits / 5 min / 5 attempts, password min 8 | ranges enforced at start-up |
| `Lis:Security:RateLimits:*` | per-minute limits (global 600, tenant 3000, credential account 20 / 10 min, client 60, challenge 120, refresh 30, mutation burst 120 at 2/s) | — |
| `Lis:Security:Envelope:*` | credential envelope: challenge 2 min, RSA key rotation 24 h, overlap 10 min, RSA 3072 | challenge ≤ 5 min, RSA ≥ 3072 |
| `Lis:Outbox:*` | outbox worker: `Enabled`, poll 2 s, batch 50, lease 30 s, 10 attempts then dead letter | — |
| `Lis:Files:Root` | uploaded files (`E:\lis-data\files`); created on demand | the service account needs modify rights (set by the install script) |
| `Lis:Platform:PortalDomain` | domain of the client portals (`<tenant>.<domain>`) | default `skylis.app` |

## 9. Logs and diagnostics

| Where | What |
| --- | --- |
| Windows **Application** event log | the API's structured log once the host runs as a service (source `Platform.WebApi`; level from `Logging:EventLog`), start-up guard refusals (`The security configuration is incomplete or weak: …`, `ROLE_GUARD_VIOLATION (…)`), crashes (`.NET Runtime`) |
| `audit.security_events` (database) | every security event with its tenant: sign-in failures, lockouts, CSRF rejections, relaxation flags, role-guard violations |
| `audit.audit_trail`, `audit.field_changes` | hash-chained business audit; `GET /api/v1/audit/verify-chain` [audit.view] verifies it |
| `%SystemDrive%\inetpub\logs\LogFiles\W3SVC<site id>` | IIS access log of `lis-ui` (proxied requests included; rewritten URLs are not logged) |
| `http://127.0.0.1:5100/health/live`, `/health/ready` | process alive; database reachable and no pending migration (503 with the count otherwise) |
| `https://<host>/.well-known/jwks.json` | the current signing key (compare `kid` after a rotation) |
| `sc.exe query NTLIS`, `Get-Service NTLIS` | service state; `sc.exe qfailure NTLIS` the restart policy |

A start-up refusal leaves the service stopped: fix `C:\lis-app\NTLIS\config\appsettings.Production.json`, then
`install-api-service.ps1 -PackagePath <same package>` (re-validates, re-installs the file, restarts).

## 10. Backup and restore drill

- **Daily**: the scheduled task of §5.8 writes `E:\lis-backups\lis-<yyyyMMdd-HHmmss>.dump` (`pg_dump -Fc`, as
  `lis_owner`), a `.sha256` sidecar, verifies the archive with `pg_restore --list` and deletes dumps older than the
  retention. Alert when the folder has no dump younger than 26 hours.
- **Monthly drill** (any server with the PostgreSQL client tools and access to the database host):

  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File .\restore-database.ps1 -DumpFile E:\lis-backups\lis-<stamp>.dump -TargetDatabase lis_drill -Replace -WhatIf
  powershell -NoProfile -ExecutionPolicy Bypass -File .\restore-database.ps1 -DumpFile E:\lis-backups\lis-<stamp>.dump -TargetDatabase lis_drill -Replace
  ```

  The script verifies the checksum, creates `lis_drill` (owner `lis_owner`, grants for `lis_app`), restores as
  `lis_owner` with `--no-owner --role=lis_owner`, and reports the table and migration counts; it fails if any table
  ended up owned by `lis_app`. Optionally point `NTLIS_TEST` at `lis_drill` and run the smoke test, then
  `DROP DATABASE lis_drill` as the superuser. Record the drill date and duration.
- **Production restore**: §6 step 9; never restore into `lis` while `NTLIS` runs.

## 11. Checklists

**Before the first installation**: runtime 9 present · URL Rewrite + ARR present · certificate imported · DNS points at
the server · PostgreSQL 17 reachable, `roles.sql` → `schema.sql` → `migrations.sql` applied · `E:\lis-data\postgres.env`
written and protected · configuration filled, no `<SET:` left · host is service-aware (§3.3) · every script dry-run
with `-WhatIf`.

**After every installation or upgrade**: `/health/ready` 200 on loopback · `smoke-test.ps1` exit 0 · sign-in and
silent refresh work in a browser (reload after 16 minutes stays signed in) · event log free of errors ·
`app.previous` / `lis-ui.previous` present for rollback · release recorded in README "Release history".

**Weekly**: last backup younger than 24 h · certificate expiry > 30 days · disk space of `E:\lis-data`, `E:\lis-backups`.

## 12. Open questions and assumptions

- **A1 (closed)** The WebApi is service-aware since 0.3.0 (§3.3); the alternative of hosting under IIS in-process was
  not taken (it would change the bind guard and the pipeline assumptions).
- **A2** Logging goes to the Windows event log through the hosting defaults; no rolling file log exists. If operations
  prefer files, a logging provider has to be added to the host (application change).
- **A3** The ARR proxy does not preserve the public `Host` header by default; the API therefore relies on
  `Lis:Security:AllowedOrigins` (always set) rather than on the request's own host for the origin check.
- **A4** The test lane uses database `lis_test` on the same PostgreSQL instance; a separate instance is equally
  possible (only the connection string changes).
- **A5** The Node commands of `deploy/SECRETS.md` are the reference for key generation; OpenSSL is an alternative for
  the P-256 key. No key material is ever generated on the server by these scripts.
- **A6** Public DNS name, certificate authority and the `PortalDomain` value are decisions of the business owner and
  are not fixed here.
