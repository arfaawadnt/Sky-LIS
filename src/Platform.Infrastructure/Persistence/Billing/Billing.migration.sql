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

-- ---------------------------------------------------------------------------------------------------------------------
-- 2026-10-19 Payment methods (change request LIS-CR-003, P-C10): the set-up list app.payment_methods, method_code on
-- payments and refunds. Appended to the PrototypeGaps migration after EF created the table and the columns. Idempotent.
-- ---------------------------------------------------------------------------------------------------------------------

-- (1) Every existing client starts with the four default methods (CASH, CARD, POS, TRANSFER); the platform tenant takes
--     none and a tenant that already defined a code keeps its own row (ON CONFLICT DO NOTHING on (tenant_id, code)).
INSERT INTO app.payment_methods (tenant_id, id, code, name_en, name_ar, kind, requires_reference, is_active, sequence, created_at_utc)
SELECT t.id, gen_random_uuid(), d.code, d.name_en, d.name_ar, d.kind, d.requires_reference, true, d.sequence, now()
FROM saas.tenants t
CROSS JOIN (VALUES
    ('CASH', 'Cash', 'نقدي', 'Cash', false, 1),
    ('CARD', 'Card', 'بطاقة', 'Card', true, 2),
    ('POS', 'POS', 'نقطة بيع', 'Pos', true, 3),
    ('TRANSFER', 'Bank transfer', 'تحويل بنكي', 'Transfer', true, 4)) AS d (code, name_en, name_ar, kind, requires_reference, sequence)
WHERE t.id <> '00000000-0000-0000-0000-000000000001'
ON CONFLICT (tenant_id, code) DO NOTHING;

-- (2) The payment guard keeps method_code immutable like the other posting facts (a payment changes only by its void).
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
       OR NEW.method_code IS DISTINCT FROM OLD.method_code
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

-- ---------------------------------------------------------------------------------------------------------------------
-- 2026-10-20 Treasury removed (change request LIS-CR-004, P-API). Appended to the TreasuryRemoved migration after EF made
-- treasury_id / device_id nullable on app.payments, app.refunds and app.cash_closings, dropped their foreign keys to
-- app.treasuries and replaced the (tenant_id, treasury_id, …) indexes by (tenant_id, branch_id, …). Idempotent.
-- ---------------------------------------------------------------------------------------------------------------------

-- Reviewed: app.billing_guard_payment() compares treasury_id and device_id with IS DISTINCT FROM, so a payment posted with
-- NULLs stays immutable like the others and its void still passes; app.billing_refuse_change() names no column. No
-- function or trigger needs a change. app.treasuries and app.treasury_access_requests stay as orphan tables (history);
-- old movements and closings keep their treasury_id and device_id. Nothing to backfill: new rows simply carry NULL.
