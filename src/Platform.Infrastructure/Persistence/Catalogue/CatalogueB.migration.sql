-- Catalogue B (intake LIS-INT-CTB-001), appended to the wave migration by scripts/add-module-sql-to-migration.mjs.
-- INV-CTB-003: English names are unique per tenant, case-insensitively (per kind for reasons). The handlers refuse
-- duplicates first with CAT-301; these indexes are the second line (UNIQUE-409). Idempotent; nothing tenant-specific
-- is seeded.

CREATE UNIQUE INDEX IF NOT EXISTS ux_sending_labs_name_en ON app.sending_labs (tenant_id, lower(name_en));
CREATE UNIQUE INDEX IF NOT EXISTS ux_test_rules_name_en ON app.test_rules (tenant_id, lower(name_en));
CREATE UNIQUE INDEX IF NOT EXISTS ux_reasons_kind_name_en ON app.reasons (tenant_id, kind, lower(name_en));
CREATE UNIQUE INDEX IF NOT EXISTS ux_report_templates_name_en ON app.report_templates (tenant_id, lower(name_en));
CREATE UNIQUE INDEX IF NOT EXISTS ux_drug_types_name_en ON app.drug_types (tenant_id, lower(name_en));
CREATE UNIQUE INDEX IF NOT EXISTS ux_commercial_names_name_en ON app.commercial_names (tenant_id, lower(name_en));
