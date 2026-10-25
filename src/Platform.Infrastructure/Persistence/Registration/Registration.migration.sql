-- Registration (wave 2, intake LIS-INT-REG-001): extra SQL the integrator appends to the wave migration after the tables
-- app.visits, app.visit_lines, app.visit_tubes, app.visit_preparation_answers, app.visit_transfers, app.bookings and
-- app.batches exist (RLS comes from scripts/add-rls-to-migration.mjs). Idempotent: CREATE OR REPLACE, DROP TRIGGER IF EXISTS.

-- BR-15 / INV-REG-006: nothing of the module is ever deleted, for every role (cancellation, transfer and conversion keep
-- the record with reason, actor and time).
CREATE OR REPLACE FUNCTION app.registration_refuse_delete() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    RAISE EXCEPTION 'registration records are never deleted: DELETE on %.% is not allowed', TG_TABLE_SCHEMA, TG_TABLE_NAME;
END
$fn$;

-- BR-01 / INV-REG-001: the accession, the patient file it was registered on and the registration time never change;
-- the patient may change only by a merge re-point, which keeps the first file in original_patient_id.
CREATE OR REPLACE FUNCTION app.visits_guard() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
       OR NEW.id IS DISTINCT FROM OLD.id
       OR NEW.accession IS DISTINCT FROM OLD.accession
       OR NEW.branch_id IS DISTINCT FROM OLD.branch_id
       OR NEW.registered_at_utc IS DISTINCT FROM OLD.registered_at_utc
       OR NEW.registered_by_user_id IS DISTINCT FROM OLD.registered_by_user_id THEN
        RAISE EXCEPTION 'the identity of a visit never changes (visit %)', OLD.accession;
    END IF;

    IF NEW.patient_id IS DISTINCT FROM OLD.patient_id AND NEW.original_patient_id IS NULL THEN
        RAISE EXCEPTION 'a visit changes patient only by a merge that keeps the original file (visit %)', OLD.accession;
    END IF;

    RETURN NEW;
END
$fn$;

-- BR-01: a tube's sample number and key never change.
CREATE OR REPLACE FUNCTION app.visit_tubes_guard() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
       OR NEW.id IS DISTINCT FROM OLD.id
       OR NEW.visit_id IS DISTINCT FROM OLD.visit_id
       OR NEW.sample_number IS DISTINCT FROM OLD.sample_number
       OR NEW.clinical_group_id IS DISTINCT FROM OLD.clinical_group_id
       OR NEW.sample_type_id IS DISTINCT FROM OLD.sample_type_id
       OR NEW.sample_condition_id IS DISTINCT FROM OLD.sample_condition_id THEN
        RAISE EXCEPTION 'a tube never changes (sample %)', OLD.sample_number;
    END IF;

    RETURN NEW;
END
$fn$;

-- BR-15: a cancelled line is frozen; a line never moves to another visit or service.
CREATE OR REPLACE FUNCTION app.visit_lines_guard() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    IF OLD.status = 'Cancelled' THEN
        RAISE EXCEPTION 'a cancelled service line is frozen (line %)', OLD.id;
    END IF;

    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
       OR NEW.id IS DISTINCT FROM OLD.id
       OR NEW.visit_id IS DISTINCT FROM OLD.visit_id
       OR NEW.service_id IS DISTINCT FROM OLD.service_id
       OR NEW.kind IS DISTINCT FROM OLD.kind
       OR NEW.parent_line_id IS DISTINCT FROM OLD.parent_line_id
       OR NEW.ordered_at_utc IS DISTINCT FROM OLD.ordered_at_utc
       OR NEW.ordered_by_user_id IS DISTINCT FROM OLD.ordered_by_user_id THEN
        RAISE EXCEPTION 'the identity of a service line never changes (line %)', OLD.id;
    END IF;

    RETURN NEW;
END
$fn$;

-- The transfer log is append-only.
CREATE OR REPLACE FUNCTION app.visit_transfers_refuse_change() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    RAISE EXCEPTION 'the transfer log is append-only: % on app.visit_transfers is not allowed', TG_OP;
END
$fn$;

DROP TRIGGER IF EXISTS visits_no_delete ON app.visits;
CREATE TRIGGER visits_no_delete BEFORE DELETE ON app.visits FOR EACH ROW EXECUTE FUNCTION app.registration_refuse_delete();
DROP TRIGGER IF EXISTS visits_guard ON app.visits;
CREATE TRIGGER visits_guard BEFORE UPDATE ON app.visits FOR EACH ROW EXECUTE FUNCTION app.visits_guard();

DROP TRIGGER IF EXISTS visit_lines_no_delete ON app.visit_lines;
CREATE TRIGGER visit_lines_no_delete BEFORE DELETE ON app.visit_lines FOR EACH ROW EXECUTE FUNCTION app.registration_refuse_delete();
DROP TRIGGER IF EXISTS visit_lines_guard ON app.visit_lines;
CREATE TRIGGER visit_lines_guard BEFORE UPDATE ON app.visit_lines FOR EACH ROW EXECUTE FUNCTION app.visit_lines_guard();

DROP TRIGGER IF EXISTS visit_tubes_no_delete ON app.visit_tubes;
CREATE TRIGGER visit_tubes_no_delete BEFORE DELETE ON app.visit_tubes FOR EACH ROW EXECUTE FUNCTION app.registration_refuse_delete();
DROP TRIGGER IF EXISTS visit_tubes_guard ON app.visit_tubes;
CREATE TRIGGER visit_tubes_guard BEFORE UPDATE ON app.visit_tubes FOR EACH ROW EXECUTE FUNCTION app.visit_tubes_guard();

DROP TRIGGER IF EXISTS visit_preparation_answers_no_delete ON app.visit_preparation_answers;
CREATE TRIGGER visit_preparation_answers_no_delete BEFORE DELETE ON app.visit_preparation_answers FOR EACH ROW EXECUTE FUNCTION app.registration_refuse_delete();

DROP TRIGGER IF EXISTS visit_transfers_append_only ON app.visit_transfers;
CREATE TRIGGER visit_transfers_append_only BEFORE UPDATE OR DELETE ON app.visit_transfers FOR EACH ROW EXECUTE FUNCTION app.visit_transfers_refuse_change();

DROP TRIGGER IF EXISTS bookings_no_delete ON app.bookings;
CREATE TRIGGER bookings_no_delete BEFORE DELETE ON app.bookings FOR EACH ROW EXECUTE FUNCTION app.registration_refuse_delete();

DROP TRIGGER IF EXISTS batches_no_delete ON app.batches;
CREATE TRIGGER batches_no_delete BEFORE DELETE ON app.batches FOR EACH ROW EXECUTE FUNCTION app.registration_refuse_delete();

-- ---------------------------------------------------------------------------------------------------------------------
-- 2026-10-24 Collected outside per sample (change request LIS-CR-008, P-SMP, reviewer point C-02). Appended to the
-- ReviewFeatures migration after EF added app.visit_tubes.collected_outside (boolean, default false),
-- app.visit_tubes.collected_outside_at_utc (timestamptz, null) and app.samples.collected_outside (boolean, default false).
-- Idempotent: each UPDATE touches only rows still false.
-- ---------------------------------------------------------------------------------------------------------------------

-- (1) The visit-level flag of the old contract meant "every tube collected outside": copy it to every tube of the visits
--     that carry it. app.visits.collected_outside stays as history (since this change it is true only for a visit
--     registered with the old visit-level field, which marks every tube, so a rerun changes nothing). The tube guard
--     (app.visit_tubes_guard) protects the number and key only, so this UPDATE passes it.
UPDATE app.visit_tubes t
SET collected_outside = true
FROM app.visits v
WHERE v.tenant_id = t.tenant_id
  AND v.id = t.visit_id
  AND v.collected_outside
  AND NOT t.collected_outside;

-- (2) The first sample of such a tube (generation 0) shows the "Outside" tag and prints "EXT"; a replacement sample was
--     collected again in the laboratory and stays false. Its status and times are left as they were recorded.
UPDATE app.samples s
SET collected_outside = true
FROM app.visit_tubes t
WHERE t.tenant_id = s.tenant_id
  AND t.id = s.tube_id
  AND t.collected_outside
  AND s.generation = 0
  AND NOT s.collected_outside;
