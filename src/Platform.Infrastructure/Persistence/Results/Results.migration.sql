-- Results (wave 3, intake LIS-INT-RES-001): extra SQL the integrator appends to the wave migration after the tables
-- app.results and app.result_log_entries exist. Idempotent: CREATE OR REPLACE, guarded trigger creation.

-- INV-RES-010 / BR-15: the result log is evidence (LDM §5.3 RL / SL): nobody, the owner included, updates or deletes a
-- row; and a result is never deleted (a cancelled line cancels its results). Retention purges, when built, run as a
-- dedicated maintenance role.
CREATE OR REPLACE FUNCTION app.results_refuse_change() RETURNS trigger
    LANGUAGE plpgsql AS
$$
BEGIN
    RAISE EXCEPTION 'results are never erased: % on %.% is not allowed', TG_OP, TG_TABLE_SCHEMA, TG_TABLE_NAME USING ERRCODE = '42501';
END
$$;

DO $$
BEGIN
    IF to_regclass('app.result_log_entries') IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM pg_trigger WHERE tgname = 'trg_result_log_entries_append_only' AND tgrelid = 'app.result_log_entries'::regclass) THEN
        CREATE TRIGGER trg_result_log_entries_append_only BEFORE UPDATE OR DELETE ON app.result_log_entries
            FOR EACH ROW EXECUTE FUNCTION app.results_refuse_change();
    END IF;

    IF to_regclass('app.results') IS NOT NULL AND NOT EXISTS (
        SELECT 1 FROM pg_trigger WHERE tgname = 'trg_results_never_deleted' AND tgrelid = 'app.results'::regclass) THEN
        CREATE TRIGGER trg_results_never_deleted BEFORE DELETE ON app.results
            FOR EACH ROW EXECUTE FUNCTION app.results_refuse_change();
    END IF;
END
$$;
