-- Reporting (intake LIS-INT-REP-001, Release 1 scope: SA-03 platform activity). Appended by the integrator to the wave
-- migration; plain SQL, safe to run twice (CREATE OR REPLACE, idempotent grants). Pattern of saas.client_usage_counts
-- (PlatformAdmin.migration.sql): the function is created by the migration role (lis_owner, BYPASSRLS), runs as its owner
-- (SECURITY DEFINER), answers counts only, and only to a session whose tenant is the platform tenant. The application
-- never elevates for these figures.

GRANT USAGE ON SCHEMA saas TO lis_app;

-- SA-03 "served patients, tests and registrations per period" across the clients, one row per tenant with activity:
--   registrations   = visits registered in [p_from, p_to)                       (app.visits, MODULES.md §5)
--   served_patients = distinct patients of those visits
--   tests           = results of those visits that are not cancelled             (app.results, MODULES.md §5; 0 before Results)
-- Tables that do not exist yet answer nothing (no row), never an error; the statements are dynamic for that reason.
CREATE OR REPLACE FUNCTION saas.client_activity_counts(p_from timestamptz, p_to timestamptz)
RETURNS TABLE (tenant_id uuid, served_patients bigint, tests bigint, registrations bigint)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path = pg_catalog, pg_temp
AS $fn$
#variable_conflict use_column
BEGIN
    IF coalesce(current_setting('app.current_tenant', true), '') <> '00000000-0000-0000-0000-000000000001' THEN
        RAISE EXCEPTION 'saas.client_activity_counts is reserved to the platform tenant' USING ERRCODE = '42501';
    END IF;

    IF p_from IS NULL OR p_to IS NULL OR p_from >= p_to OR to_regclass('app.visits') IS NULL THEN
        RETURN;
    END IF;

    IF to_regclass('app.results') IS NULL THEN
        RETURN QUERY EXECUTE
            'SELECT v.tenant_id, count(DISTINCT v.patient_id), 0::bigint, count(*)
             FROM app.visits v
             WHERE v.registered_at_utc >= $1 AND v.registered_at_utc < $2
             GROUP BY v.tenant_id'
            USING p_from, p_to;
    ELSE
        RETURN QUERY EXECUTE
            'SELECT v.tenant_id, count(DISTINCT v.patient_id), coalesce(sum(t.tests), 0)::bigint, count(*)
             FROM app.visits v
             CROSS JOIN LATERAL (
                 SELECT count(*) AS tests FROM app.results r
                 WHERE r.tenant_id = v.tenant_id AND r.visit_id = v.id AND r.status <> ''Cancelled''
             ) t
             WHERE v.registered_at_utc >= $1 AND v.registered_at_utc < $2
             GROUP BY v.tenant_id'
            USING p_from, p_to;
    END IF;
END
$fn$;
REVOKE ALL ON FUNCTION saas.client_activity_counts(timestamptz, timestamptz) FROM PUBLIC;
GRANT EXECUTE ON FUNCTION saas.client_activity_counts(timestamptz, timestamptz) TO lis_app;
