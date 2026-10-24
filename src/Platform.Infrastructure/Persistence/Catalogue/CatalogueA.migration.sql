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

-- ---------------------------------------------------------------------------------------------------------------------
-- 2026-10-24 Culture specimens (change request LIS-CR-007, P-MIC, reviewer point C-11): app.sample_types.for_culture
-- marks the sample types used by cultures (Microbiology › Culture specimens, the culture service's sample-type picker).
-- Appended to the ReviewQuickWins migration after EF added the column (NOT NULL DEFAULT false). The existing culture
-- specimens of every tenant — the platform's global templates included, so new laboratories copy the flag — are the
-- types whose English name names one: swab, culture, urine, stool, sputum, pus, CSF, wound. Only sets the flag (never
-- clears one), so a rerun changes nothing. Idempotent.
-- ---------------------------------------------------------------------------------------------------------------------
UPDATE app.sample_types
SET for_culture = true
WHERE NOT for_culture
  AND name_en ILIKE ANY (ARRAY['%swab%', '%culture%', '%urine%', '%stool%', '%sputum%', '%pus%', '%csf%', '%wound%']);
