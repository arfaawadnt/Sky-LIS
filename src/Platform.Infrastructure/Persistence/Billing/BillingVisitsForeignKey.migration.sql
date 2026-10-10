-- The account of a visit references the visit (id = the visit id). Billing.migration.sql declares the same guarded block,
-- but it ran before Registration created app.visits (wave order), so it is repeated here once the table exists. Idempotent.
DO $$
BEGIN
    IF to_regclass('app.visits') IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_visit_accounts_visits' AND conrelid = 'app.visit_accounts'::regclass) THEN
        ALTER TABLE app.visit_accounts
            ADD CONSTRAINT fk_visit_accounts_visits FOREIGN KEY (tenant_id, id) REFERENCES app.visits (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END
$$;
