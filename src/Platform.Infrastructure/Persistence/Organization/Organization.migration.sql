-- Organization module (intake LIS-INT-ORG-001), appended by the integrator to the wave-1 migration after the tables
-- app.branches, app.roles (extended) and app.role_result_rights exist. Idempotent. RLS for the new tables comes from
-- scripts/add-rls-to-migration.mjs; nothing here touches app.bypass_rls.

-- INV-ORG-006: role names are unique per tenant, English form, case-insensitive (EF cannot express the expression index).
CREATE UNIQUE INDEX IF NOT EXISTS ux_roles_tenant_name_en ON app.roles (tenant_id, lower(name_en));

-- INV-ORG-009: the discount cap of a role or of a user's override is empty (tenant default) or 0..100.
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_roles_special_discount_cap_percent_range') THEN
        ALTER TABLE app.roles
            ADD CONSTRAINT ck_roles_special_discount_cap_percent_range
            CHECK (special_discount_cap_percent IS NULL OR special_discount_cap_percent BETWEEN 0 AND 100);
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_tenant_memberships_override_discount_cap_percent_range') THEN
        ALTER TABLE app.tenant_memberships
            ADD CONSTRAINT ck_tenant_memberships_override_discount_cap_percent_range
            CHECK (override_discount_cap_percent IS NULL OR override_discount_cap_percent BETWEEN 0 AND 100);
    END IF;
    -- INV-ORG-001: branch codes are stored upper-case.
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_branches_code_upper') THEN
        ALTER TABLE app.branches ADD CONSTRAINT ck_branches_code_upper CHECK (code = upper(code));
    END IF;
END
$$;
