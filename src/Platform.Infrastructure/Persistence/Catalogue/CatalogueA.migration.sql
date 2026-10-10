-- Catalogue half A (intake LIS-INT-CAT-001, INV-CAT-004): the service sequence is unique per tenant, checked at
-- commit so that the bulk save of CP-42 can swap two sequences in one transaction (EF issues one UPDATE per row).
-- The handler refuses duplicates first with CAT-105; this constraint is the backstop. Idempotent.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ux_services_sequence'
          AND conrelid = 'app.services'::regclass
    ) THEN
        ALTER TABLE app.services
            ADD CONSTRAINT ux_services_sequence UNIQUE (tenant_id, sequence) DEFERRABLE INITIALLY DEFERRED;
    END IF;
END
$$;
