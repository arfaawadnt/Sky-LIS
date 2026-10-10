-- Integration module (intake LIS-INT-INT-001): extra SQL appended by the integrator to the wave-3 migration. Idempotent.
-- The tables, keys, indexes and CHECK constraints come from the EF model; FORCE RLS + tenant_isolation on every app.*
-- table of the module (analysers, analyser_flags, analyser_test_mappings, analyser_value_conversions,
-- analyser_sample_codes, site_agents, pcr_kits, pcr_kit_genes, site_agent_states, analyser_connection_states,
-- agent_log_entries, analyser_result_intake) come from scripts/add-rls-to-migration.mjs.
-- identity.machine_credentials has no RLS (like identity.refresh_sessions: read by client id before a tenant is known);
-- lis_app already holds SELECT, INSERT, UPDATE on the identity schema through its default privileges (schema.sql).

-- 1. The platform library of analyser models (SA-15) is read by tenants (CP-58 model picker): SELECT only, when present.
DO $$
BEGIN
    IF to_regclass('saas.analyser_models') IS NOT NULL THEN
        GRANT USAGE ON SCHEMA saas TO lis_app;
        GRANT SELECT ON saas.analyser_models TO lis_app;
    END IF;
END
$$;

-- 2. Append-only logs: the agent log and the result intake are evidence (WF-07 "everything pending is traced"); nobody,
--    the owner included, updates or deletes a row. Retention purges, when built, run as a dedicated maintenance role.
CREATE OR REPLACE FUNCTION app.integration_refuse_change() RETURNS trigger
    LANGUAGE plpgsql AS
$$
BEGIN
    RAISE EXCEPTION 'append-only table %.%: % refused', TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = '42501';
END
$$;

DO $$
BEGIN
    IF to_regclass('app.agent_log_entries') IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM pg_trigger WHERE tgname = 'trg_agent_log_entries_append_only' AND tgrelid = 'app.agent_log_entries'::regclass) THEN
        CREATE TRIGGER trg_agent_log_entries_append_only BEFORE UPDATE OR DELETE ON app.agent_log_entries
            FOR EACH ROW EXECUTE FUNCTION app.integration_refuse_change();
    END IF;

    IF to_regclass('app.analyser_result_intake') IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM pg_trigger WHERE tgname = 'trg_analyser_result_intake_append_only' AND tgrelid = 'app.analyser_result_intake'::regclass) THEN
        CREATE TRIGGER trg_analyser_result_intake_append_only BEFORE UPDATE OR DELETE ON app.analyser_result_intake
            FOR EACH ROW EXECUTE FUNCTION app.integration_refuse_change();
    END IF;
END
$$;
