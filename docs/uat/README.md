# Release-1 UAT — how to run a session and record results

Scripts: `Release-1_UAT_Scripts.md` (LIS-UAT-001). This page is the procedure; the scripts are the content.

## 1. Before the session

1. **Environment**: the test lane of the deployment runbook (`docs/deploy/Release-1_Deployment_Runbook.md` §7) on the
   build under test, with a certificate (plain http loses the session after 15 minutes). Note the `VERSION` of the
   package on every results sheet. The test lane holds **no real patient data**; UAT uses invented names only.
2. **Seed** (runbook §5.7, DevTools against `lis_test`): `seed-platform-admin platform.admin <pw>` and
   `seed-demo-lab lab.admin <pw>`. Hand the one-time passwords to the test lead only. Do not reuse a database that a test
   suite has run against (the suites truncate it); reseed instead.
3. **Workstations**: one browser profile per persona (the cashier's treasury approval is bound to the browser profile,
   BR-27); a PDF printer; the notification gateway of the lane configured or the *gateway* scripts marked blocked.
4. **People**: a test lead (owns the run order, the results sheets and the defect log), one tester per persona drawn from
   the laboratory's actual staff for that role (reception, cashier, technician, unit head, administrator, accounts) and
   from National Technology for the Super Admin personas, a scribe, and a developer on call who never drives the browser.
5. **Run order** (LIS-UAT-001 §2): Super Admin roles and users → laboratory administrator set-up (branches, roles, users,
   treasuries, catalogue additions) → accounts (payers, lists) → cashier access request → reception → samples → results →
   verification → remaining cashier, accounts, Super Admin scripts. A script's preconditions name the scripts it depends on.

## 2. During the session

- The tester executes the steps **exactly as written** and reads the expected result aloud before judging; the scribe
  fills the results sheet (LIS-UAT-001 §6): script id, tester, date, build, Pass / Fail / Blocked, failed step, defect
  reference, severity, notes.
- A step that differs from the expected result **fails the script**. Finish the remaining steps when they are still
  possible (more findings per run), then log one defect per distinct cause with: script id and step, what was done, what
  was expected (quote the SRS page / BR / code), what happened, screenshot or printed PDF, severity S1–S4 (scale in §6 of
  the scripts). Keep the printed documents (labels, job order, receipts, reports) with the sheet.
- Negative steps pass only when the **named refusal** appears (code and message); a different refusal, or a silent
  failure, is a defect.
- The Arabic check at the end of every script is mandatory; an untranslated label is an S3 defect, a broken
  right-to-left layout is an S2.
- Questions about intended behaviour that the SRS does not answer are **not** defects: record them in the "open points"
  list of the session (LIS-UAT-001 §9 holds the known ones) for the business owner.
- Nobody changes data through the database, DevTools or the API during a session; everything happens through the portals,
  so the audit trail of the run is complete.

## 3. After the session

1. Save the results sheets as `docs/uat/results/<yyyy-mm-dd>_<persona>.md` (one table per persona, the template of
   LIS-UAT-001 §6) and the defect log as `docs/uat/results/<yyyy-mm-dd>_defects.md`. Printed PDFs go next to them under
   `docs/uat/results/<yyyy-mm-dd>/` (invented data only; the repository is public — never commit anything that looks like
   real patient, staff or client data).
2. Triage with the developer on call: confirm severity, assign, and decide per defect whether the fix is in Release 1 or
   deferred with the business owner's agreement.
3. **Re-test** every failed script on the fixed build (whole script, not only the failed step) and record the new run on a
   new sheet; never overwrite a previous result.
4. **Acceptance** per persona (LIS-UAT-001 §7): all critical scripts pass on the same build, no open S1 against the persona,
   every failed standard script has an agreed severity and target. The persona's tester signs the sheet. The laboratory
   signs when every client-portal persona has accepted; National Technology when the three Super Admin personas have.
5. Feed the open points and the confirmed assumptions back into the SRS (`docs/srs/src/srs.data.js`, bump the version)
   and the intakes, and record the accepted build in the README release history before the go-live push.
