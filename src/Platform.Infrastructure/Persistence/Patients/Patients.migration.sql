-- Patients (wave 1, intake LIS-INT-PAT-001): extra SQL the integrator appends to the wave migration after
-- app.patients and app.patient_documents exist (RLS comes from scripts/add-rls-to-migration.mjs).
-- Idempotent: CREATE OR REPLACE, DROP TRIGGER IF EXISTS.

-- INV-PAT-001 / INV-PAT-006 / BR-15: a patient file is never deleted, its number never changes, and a Merged file is
-- frozen (the only way out of Active is the merge, once). Enforced for every role, behind the aggregate's back too.
CREATE OR REPLACE FUNCTION app.patients_guard() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'patient files are never deleted (patient %)', OLD.number;
    END IF;

    IF OLD.status = 'Merged' THEN
        RAISE EXCEPTION 'a merged patient file is frozen (patient %)', OLD.number;
    END IF;

    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
       OR NEW.id IS DISTINCT FROM OLD.id
       OR NEW.number IS DISTINCT FROM OLD.number
       OR NEW.registered_on IS DISTINCT FROM OLD.registered_on THEN
        RAISE EXCEPTION 'the identity of a patient file never changes (patient %)', OLD.number;
    END IF;

    RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS patients_guard ON app.patients;
CREATE TRIGGER patients_guard BEFORE UPDATE OR DELETE ON app.patients FOR EACH ROW EXECUTE FUNCTION app.patients_guard();

-- INV-PAT-008 / INV-PAT-009 / BR-15: a document is never deleted, its content record never changes, and the only
-- change is its deactivation (once, with actor, time and reason).
CREATE OR REPLACE FUNCTION app.patient_documents_guard() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'patient documents are never deleted (document %)', OLD.id;
    END IF;

    IF NOT OLD.is_active THEN
        RAISE EXCEPTION 'a deactivated patient document is frozen (document %)', OLD.id;
    END IF;

    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
       OR NEW.id IS DISTINCT FROM OLD.id
       OR NEW.patient_id IS DISTINCT FROM OLD.patient_id
       OR NEW.file_name IS DISTINCT FROM OLD.file_name
       OR NEW.content_type IS DISTINCT FROM OLD.content_type
       OR NEW.size_bytes IS DISTINCT FROM OLD.size_bytes
       OR NEW.storage_key IS DISTINCT FROM OLD.storage_key
       OR NEW.sha256 IS DISTINCT FROM OLD.sha256
       OR NEW.note IS DISTINCT FROM OLD.note
       OR NEW.uploaded_by_user_id IS DISTINCT FROM OLD.uploaded_by_user_id
       OR NEW.uploaded_at_utc IS DISTINCT FROM OLD.uploaded_at_utc THEN
        RAISE EXCEPTION 'a patient document changes only by its deactivation (document %)', OLD.id;
    END IF;

    RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS patient_documents_guard ON app.patient_documents;
CREATE TRIGGER patient_documents_guard BEFORE UPDATE OR DELETE ON app.patient_documents FOR EACH ROW EXECUTE FUNCTION app.patient_documents_guard();
