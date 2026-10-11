# Sky LIS — installation record

One row per installation or upgrade performed with the scripts of `deploy/windows`. The runbook is
`Release-1_Deployment_Runbook.md`; the record keeps what the runbook cannot know: which host, which version, which
certificate, what differs from the standard layout.

## WIN-9S2KO4FA64L (development server of National Technology) — production lane, self-signed

| Item | Value |
| --- | --- |
| Installed | 2026-10-10, package 0.3.2 (commit `1af25d9`); upgraded the same day to **0.3.5** (service and site, configuration kept, smoke test 20 / 20); upgraded on 2026-10-11 to **0.4.0** (all validation fixes; migration `AppendOnlyTruncateGuard` applied with `migrations.sql`; smoke test 20 / 20 by name and 17 / 17 by IP; backend-cycle deployed run 5 / 5). The first 0.4.0 attempt failed to move the old build while the service process was still exiting (a few minutes of downtime, old build intact); the installer now waits for the process and retries. Upgraded on 2026-10-11 to **0.5.0** (commit `7a2f6ea`, registration page redesign, frontend only; backup `E:\lis-backups\lis-20261011-230609.dump` taken first with a temporary env file pointing at port 5433, because `E:\lis-data\postgres.env` does not exist on this host; `migrations.sql` re-run, nothing pending; service down about 2 minutes; smoke test 20 / 20 by name and 17 / 17 by IP; the deployed bundle carries the new page). All built with `publish.ps1 -SkipNpmCi` |
| API service | `NTLIS`, `C:\lis-app\NTLIS`, account `NT SERVICE\NTLIS`, `http://127.0.0.1:5100`, delayed-auto start, failure restarts |
| Configuration | `C:\lis-app\NTLIS\config\appsettings.Production.json` (ACL: SYSTEM, Administrators, service read). Same signing, subject-encryption and master keys as the dev host of this machine (user-secrets), so the dev API and the service share one identity and one database |
| Database | `lis` on the **private PostgreSQL 17 instance** `127.0.0.1:5433` (`E:\LIS\.local\pg17`), now the Windows service `LIS_POSTGRES` (automatic start, account `NT AUTHORITY\NetworkService`, options `-p 5433 -c listen_addresses=127.0.0.1`). Not the standard layout (the runbook assumes the PostgreSQL service on 5432): this host keeps the other application's cluster on 5432 untouched |
| IIS site | `lis-ui`, `C:\inetpub\wwwroot\lis-ui`, app pool `lis-ui`, bindings `https://WIN-9S2KO4FA64L:443` (SNI, next to the other application's catch-all `*:443`) and `https://*:5443` (any host name or the IP `192.168.80.38`, for the LAN; firewall rule "Sky LIS site (lis-ui) HTTPS 5443", domain and private profiles); ARR proxy was already enabled on the server |
| Certificate | self-signed `CN=WIN-9S2KO4FA64L` (SANs `WIN-9S2KO4FA64L`, `localhost`), thumbprint `80ED99F0ACCB4F737E4ECCA3D7205DFD1790B1C7`, expires 2028-10-10, `Cert:\LocalMachine\My`. Browsers warn until a trusted certificate replaces it (`install-ui-site.ps1 -CertificateThumbprint <new>`) |
| Origins | `Lis:Security:AllowedOrigins` = `https://win-9s2ko4fa64l`, `https://WIN-9S2KO4FA64L`, `https://192.168.80.38:5443`, `https://win-9s2ko4fa64l:5443`, `https://WIN-9S2KO4FA64L:5443`; `Lis:Platform:PortalDomain` = `win-9s2ko4fa64l` (no wildcard DNS on this host: client portal addresses are informational) |
| Files root | `E:\lis-data\files` (created by the installer) |
| Smoke test | `smoke-test.ps1 -BaseUrl https://WIN-9S2KO4FA64L -ApiUrl http://127.0.0.1:5100 -SkipCertificateCheck`: 20 / 20 |
| Data | the database holds the development and UAT dry-run data (demo laboratory, "Walk-through Laboratory", test patients and visits); the tenant parameters `autoPrintLabels`, `autoPrintJobOrder`, `autoPrintReceipt` of the demo laboratory were switched off for the automated dry run |
| Not installed | the test lane (`NTLIS_TEST`, `lis-ui-test`), a scheduled backup task, an SMS/e-mail gateway |
| Rollback | `install-ui-site.ps1 -Rollback`, `install-api-service.ps1 -Rollback`; database via `restore-database.ps1` with the service stopped |
| Uninstall | `sc.exe delete NTLIS` after `Stop-Service NTLIS`; remove the site and pool with the WebAdministration module; `sc.exe delete LIS_POSTGRES` returns the instance to console use (`pg_ctl start`) |

Accounts for a manual check are in the git-ignored `.local/dev-credentials.md` of the repository on this host.
