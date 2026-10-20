# Change request 2026-10-20 — Separate sign-in addresses: one per laboratory, one for the Super Admin (LIS-CR-005)

Decision of the business owner (2026-10-20): the Super Admin portal and the client laboratories no longer share one sign-in;
**every laboratory has its own address and the Super Admin has its own**, all told apart by the URL. Form chosen: **path only**
(works on every installation, by host name and by IP, without wildcard DNS or certificates):

| Address | Opens | Accepts |
| --- | --- | --- |
| `https://<host>/lab/<slug>` | the sign-in of that laboratory (its name and logo shown) | members of that laboratory only |
| `https://<host>/lab/<slug>/recover` | password recovery started from that laboratory | — |
| `https://<host>/admin/login` | the Super Admin sign-in | platform staff (members of `TenantIds.Platform`) only |
| `https://<host>/admin/login/recover` | password recovery started from the Super Admin sign-in | — |
| `https://<host>/login`, an anonymous `https://<host>/` | **Find your laboratory**: one box for the laboratory address (slug), *Continue* → `/lab/<slug>`; links to *Subscribe now* and the public site; no username / password form | — |

`<slug>` is the tenant's existing `PortalSlug` (`saas.tenants.portal_slug`: 3–40 of `a-z 0-9 -`, unique, reserved words refused —
INV-PLT-001). After sign-in the application's URLs stay as they are today (the tenant comes from the session, never from the
URL); the address only decides **where one signs in and where one returns**. Read with `docs/build/MODULE-PATTERN.md` §2 and the
rules of `CR-2026-10-19_Prototype-Gaps.md` §0 (slots, forbidden files, commands, reports).

## 0. Target shape

**Sign-in request** (`SignInRequest`, `POST /api/v1/auth/sign-in`): `{ username, password, laboratory?: string, admin?: bool }` —
**exactly one** of `laboratory` (a slug) or `admin: true` (`VALIDATION-400` otherwise). The old `tenant` (Guid) field and the
`select-tenant` step are **removed**: a laboratory address names one tenant, so a user with memberships in several laboratories
signs in at each laboratory's address.

- `laboratory`: the slug (lower-cased, trimmed) is resolved to an **active** client tenant; the user must hold an active membership
  in it. An unknown slug, an inactive laboratory or a user who is not a member answer exactly like a wrong password (`AUTH-002`,
  same timing path, same security event with a reason) — the sign-in never reveals which part failed. A suspended / inactive
  laboratory keeps its existing refusal code if one exists today for an inactive client (`CLI-020`) **only after** the password
  was verified (as today).
- `admin: true`: the user must hold the active platform membership (`TenantIds.Platform`); anything else is `AUTH-002`.
- Everything else of sign-in is unchanged (credential envelope, lockout, rate limits, refresh cookie, MFA gate, password-change
  gate, the revocation of the presented refresh cookie's session).

**Session profile** (`AuthProfile`, answers of sign-in / refresh / `me`): gains `portalSlug: string | null` (the laboratory's slug;
`null` for the platform tenant), so the SPA can send a signed-out or timed-out user back to the right sign-in address.

**Public laboratory card** (anonymous, rate-limited by the existing global chain): `GET /api/v1/public/laboratories/{slug}` →
`{ slug, nameEn, nameAr, logoVersion: int | null }` for an **active** client tenant, `404` (`PLT-4xx`-style "laboratory not found")
otherwise (inactive, platform tenant, unknown, malformed); `GET /api/v1/public/laboratories/{slug}/logo` → the laboratory logo
bytes with its content type (`404` when none), `Cache-Control: public, max-age=300` plus the version in an ETag. These read only
what the sign-in page shows; nothing else about a tenant is public. Both through elevated read ports in Infrastructure (the tenant is
not resolved yet): add the new `Elevate()` sites to the allow-list gate in `WebApiHardeningTests` / wherever it lives, with a reason.

**Portal address of a laboratory** (provisioning response and SMS, subscription wizard result, admin password-reset SMS, SA-04 list
and detail, admin dashboard, CP-65 profile): `{PortalBaseUrl}/lab/{slug}` where the new setting `Lis:Platform:PortalBaseUrl` is an
absolute `https://` URL without path, query or trailing slash (ConfigGuard: refused if malformed; in Production required). Default
for development `http://127.0.0.1:4300`. `Lis:Platform:PortalDomain` and `Tenant.BuildPortalUrl(slug, domain)` are replaced
(keep reading `PortalDomain` only if removing it breaks a test you cannot fix; say so). The `portal-url` SMS template text is
unchanged (it prints `{url}`).

## 1. Packages

| Package | Scope | Slot | Owns |
| --- | --- | --- | --- |
| P-API | backend | `found` | `src/Platform.*` (Identity sign-in, AuthProfile, session principal / profile builders, PlatformAdmin portal URL, public laboratory endpoints, settings / ConfigGuard), `tools/Platform.DevTools` if it signs in, `tests/**` except `Support/` (report Support needs), `tests/validation/backend-cycle.mjs`, intakes FND-001 / PLT-001 / ACC-001 |
| P-SPA | frontend | — | `frontend/src/app/features/auth/**`, `features/public/**` (subscription wizard end), `core/auth/**` (session navigation, store sign-in, guards), `core/api/auth.contracts.ts`, new `core/api/public-laboratory.contracts.ts`, `public/i18n/auth.{en,ar}.json`, `public/i18n/public.{en,ar}.json` |

Integrator: `app.routes.ts` (top-level anonymous routes `lab/:slug`, `lab/:slug/recover`, `admin/login`, `admin/login/recover`, `login`
→ find page; the order must put them before the shell route), `shell/**` if needed, `PermissionCatalog.cs` (none expected),
`appsettings*.json` (`Lis:Platform:PortalBaseUrl`), `deploy/windows/appsettings.Production.template.json`, the server's
configuration at install, `CLAUDE.md`, `README.md`, SRS (SA-01 / CP-03 addresses, a new rule), UAT scripts, runbook,
`docs/deploy/Installations.md`.

## 2. P-API

- `SignInRequest` / `SignInCommand`: replace `Tenant` with `Laboratory` (string?) and `Admin` (bool?); validator: exactly one;
  `Laboratory` 3–40 chars of `a-z 0-9 -` after trim + lower-case (a malformed slug is `VALIDATION-400`, not AUTH-002 — shape only).
  Handler: resolve the target tenant first through a read port (`ILaboratoryAddresses` or similar: slug → tenant id + active flag;
  elevated, in Infrastructure), then pick **only** the membership of that tenant (or the platform one for admin). Keep the exact
  password-verification order and timing behaviour of today: verify the password even when the tenant or the membership is missing
  (no early return that skips hashing — use the existing dummy-hash path if there is one, else add one), and log the security event
  with a distinct reason (`unknown_laboratory`, `not_a_member`, `not_platform_staff`). Remove `TenantChoice`, the `select-tenant`
  step and `MembershipDirectory`'s multi-membership listing if nothing else uses it.
- `AuthProfile.PortalSlug` from the session principal's tenant (null for the platform); keep `SessionPrincipalQuery` to one query
  (join `saas.tenants`).
- Public endpoints of §0 in a new `PublicLaboratoriesController` under `Controllers/PlatformAdmin` (or `Account` for the logo if the
  logo store lives there), anonymous, no credential, `[AllowAnonymous]` per the existing public controllers' pattern, request types
  with the existing public policy attribute (see how `api/v1/public/governorates` is declared). Logo bytes through the existing logo
  file store (`LaboratoryLogo`, area `account`).
- `Lis:Platform:PortalBaseUrl` + `Tenant.PortalUrl(baseUrl)` = `{base}/lab/{slug}`; update every caller listed in §0; ConfigGuard rule;
  `appsettings.Development.json` value `http://127.0.0.1:4300` is the integrator's (tell me if you need it to build).
- Tests: functional — sign-in at a laboratory address (member → signed in, profile `portalSlug`), another laboratory's slug →
  AUTH-002, unknown slug → AUTH-002, inactive laboratory → as today, platform user with a laboratory slug → AUTH-002, laboratory user
  with `admin: true` → AUTH-002, platform user with `admin: true` → signed in (`portalSlug` null), neither / both → 400, malformed
  slug → 400; public card 200 / 404 (inactive, platform, unknown); logo 200 / 404 anonymous; portal URL shape in the provisioning and
  subscription answers. Unit tests for the validator and the URL builder. Update every existing test that signs in (test `Support/`
  helpers sign in too — report the exact lines; the integrator edits `Support/`).
- `tests/validation/backend-cycle.mjs`: sign in with `laboratory` / `admin`.
- Intakes: FND-001 (sign-in addresses, request shape, codes, the removed step), PLT-001 (portal base URL, public card), ACC-001
  (wizard result URL) — a dated LIS-CR-005 section each, version bumps.
- No migration is expected. If you need one, stop and report.

## 3. P-SPA

- `core/api/auth.contracts.ts`: `SignInRequest { username; password; laboratory?: string; admin?: true }`; remove `TenantChoice`
  and the `select-tenant` step; `AuthProfile.portalSlug: string | null`. `core/api/public-laboratory.contracts.ts`:
  `PublicLaboratory { slug; nameEn; nameAr; logoVersion: number | null }` + `PUBLIC_LABORATORY_API`.
- `AuthStore.signIn(username, password, target: { laboratory: string } | { admin: true })`.
- `core/auth/session-navigation.ts`: `signInUrl(returnUrl, profile)` → `/lab/<slug>` for a laboratory session, `/admin/login`
  for the platform, `/login` (the find page) when the profile is unknown; the idle variant keeps `?reason=idle`. Every caller
  (`AuthStore` sign-out, refresh failure, idle watcher, interceptors, guards: `authGuard` sends an anonymous user to `/login`
  with `returnUrl`) uses it; `isLoginRoute` recognises `/login`, `/lab/…`, `/admin/login…`. A `returnUrl` is honoured only on the
  portal it belongs to (as today).
- Pages (design 5c of 2026-10-20 kept: `features/auth/pages/login.page.*` becomes the shared sign-in, driven by route data
  `mode: 'laboratory' | 'admin'` and the `slug` param):
  - **Laboratory sign-in** `/lab/:slug`: reads the public card; shows the laboratory's logo (when any) and name in the card header
    ("Sign in to <lab name>" / "تسجيل الدخول إلى <name>"), the Sky LIS logo stays in the page header; an unknown / inactive slug shows
    a clear "This laboratory address was not found" state with a link to the find page (no form). Posts `{ laboratory: slug }`.
    `?username=` prefill and `?reason=idle` kept.
  - **Super Admin sign-in** `/admin/login`: the same layout with the hero "Sky LIS administration" / "إدارة Sky LIS" and no
    subscribe link; posts `{ admin: true }`.
  - **Find your laboratory** `/login` (`find-laboratory.page.*`): one field "Laboratory address" with the visible prefix
    `<host>/lab/` and a *Continue* button (navigates to `/lab/<slug>` after a light shape check; the laboratory page itself answers
    whether it exists), the hint "Your laboratory administrator received this address when the laboratory was registered.",
    links *Subscribe now*, public site; same 5c ground and logo.
  - **Recovery** `/lab/:slug/recover` and `/admin/login/recover` reuse `recovery.page` with the context, and return to their own
    sign-in at the end (today they go to `/login`).
  - **Subscription wizard** (`features/public/pages/subscribe.page.*`): the final step's "Go to sign in" opens
    `/lab/<slug>?username=…` (the result's `portalUrl` already points there; use the slug from the result).
- Dictionaries (English and Arabic): every new text; counted forms rule; no "(s)".
- Specs: the sign-in page in both modes (posted body, not-found state, idle notice, prefill), the find page, `signInUrl` for the
  three cases, the store's sign-in body, the wizard's sign-in link. Update existing specs that post `tenant` or expect `/login`.
- Commands as usual; never start servers; do not commit.

## 4. Integrator checklist

1. Routes (`app.routes.ts`) and any `shell` change; `appsettings.Development.json` `Lis:Platform:PortalBaseUrl`.
2. After both packages: build, all five suites (integrator slot), frontend lint / boundaries / specs / build; browser check of the four
   addresses by name and by IP form on the review stack, sign-out and idle returning to the right address, the find page.
3. Docs: SRS 1.3 (SA-01 and CP-03 addresses; new rule BR-28 "Sign-in by address"), UAT (sign-in steps use the addresses; a script
   per refusal), runbook (`PortalBaseUrl` in the configuration list), CLAUDE.md §5, README.
4. Release (minor). Install: set `Lis:Platform:PortalBaseUrl` in the server's protected configuration (record the change), upgrade,
   smoke tests, tell the user every laboratory's new address (list the active slugs).
