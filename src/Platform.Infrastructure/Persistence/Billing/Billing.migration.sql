-- Billing (wave 2, intake LIS-INT-BIL-001): extra SQL the integrator appends to the wave migration after the tables of
-- app.visit_accounts, app.payments, app.refunds and app.cash_closings (and Registration's app.visits) exist.
-- Idempotent: CREATE OR REPLACE, DROP TRIGGER IF EXISTS, guarded constraint creation.

-- INV-BIL-004 / pitfall P-54: money records are never edited in place or deleted, for every role. Refunds and closings
-- never change; a payment changes only by its void (Posted -> Voided with reason, voider and time), once.
CREATE OR REPLACE FUNCTION app.billing_refuse_change() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    RAISE EXCEPTION 'billing rows are immutable: % on %.% is not allowed', TG_OP, TG_TABLE_SCHEMA, TG_TABLE_NAME;
END
$fn$;

CREATE OR REPLACE FUNCTION app.billing_guard_payment() RETURNS trigger LANGUAGE plpgsql AS $fn$
BEGIN
    IF TG_OP = 'DELETE' THEN
        RAISE EXCEPTION 'payments are never deleted (receipt %)', OLD.receipt_number;
    END IF;

    IF OLD.status = 'Voided' THEN
        RAISE EXCEPTION 'a voided payment is frozen (receipt %)', OLD.receipt_number;
    END IF;

    IF NEW.tenant_id IS DISTINCT FROM OLD.tenant_id
       OR NEW.id IS DISTINCT FROM OLD.id
       OR NEW.visit_id IS DISTINCT FROM OLD.visit_id
       OR NEW.receipt_number IS DISTINCT FROM OLD.receipt_number
       OR NEW.amount IS DISTINCT FROM OLD.amount
       OR NEW.method IS DISTINCT FROM OLD.method
       OR NEW.reference IS DISTINCT FROM OLD.reference
       OR NEW.treasury_id IS DISTINCT FROM OLD.treasury_id
       OR NEW.device_id IS DISTINCT FROM OLD.device_id
       OR NEW.cashier_user_id IS DISTINCT FROM OLD.cashier_user_id
       OR NEW.branch_id IS DISTINCT FROM OLD.branch_id
       OR NEW.batch_id IS DISTINCT FROM OLD.batch_id
       OR NEW.paid_at_utc IS DISTINCT FROM OLD.paid_at_utc THEN
        RAISE EXCEPTION 'a payment changes only by its void (receipt %)', OLD.receipt_number;
    END IF;

    RETURN NEW;
END
$fn$;

DROP TRIGGER IF EXISTS payments_guard ON app.payments;
CREATE TRIGGER payments_guard BEFORE UPDATE OR DELETE ON app.payments FOR EACH ROW EXECUTE FUNCTION app.billing_guard_payment();

DROP TRIGGER IF EXISTS refunds_immutable ON app.refunds;
CREATE TRIGGER refunds_immutable BEFORE UPDATE OR DELETE ON app.refunds FOR EACH ROW EXECUTE FUNCTION app.billing_refuse_change();

DROP TRIGGER IF EXISTS cash_closings_immutable ON app.cash_closings;
CREATE TRIGGER cash_closings_immutable BEFORE UPDATE OR DELETE ON app.cash_closings FOR EACH ROW EXECUTE FUNCTION app.billing_refuse_change();

-- An account belongs to a visit (id = the visit id). Registration owns app.visits in the same wave, so the foreign key is
-- declared here rather than in the EF model (MODULE-PATTERN §3 "Same-wave lookups").
DO $$
BEGIN
    IF to_regclass('app.visits') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_visit_accounts_visits' AND conrelid = 'app.visit_accounts'::regclass) THEN
        ALTER TABLE app.visit_accounts
            ADD CONSTRAINT fk_visit_accounts_visits FOREIGN KEY (tenant_id, id) REFERENCES app.visits (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END
$$;
