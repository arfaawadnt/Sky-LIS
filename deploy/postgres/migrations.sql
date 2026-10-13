DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'ops') THEN
        CREATE SCHEMA ops;
    END IF;
END $EF$;
CREATE TABLE IF NOT EXISTS ops.__ef_migrations_history (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'ops') THEN
            CREATE SCHEMA ops;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'identity') THEN
            CREATE SCHEMA identity;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'app') THEN
            CREATE SCHEMA app;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    CREATE TABLE ops.outbox_events (
        id uuid NOT NULL,
        type character varying(400) NOT NULL,
        payload jsonb NOT NULL,
        tenant_id uuid,
        occurred_at_utc timestamp with time zone NOT NULL,
        enqueued_at_utc timestamp with time zone NOT NULL,
        attempts integer NOT NULL,
        next_attempt_at_utc timestamp with time zone NOT NULL,
        claimed_until_utc timestamp with time zone,
        processed_at_utc timestamp with time zone,
        dead_lettered_at_utc timestamp with time zone,
        trace_parent character varying(64),
        last_error text,
        CONSTRAINT pk_outbox_events PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    CREATE TABLE identity.platform_users (
        id uuid NOT NULL,
        username character varying(64) NOT NULL,
        email character varying(254) NOT NULL,
        display_name character varying(200) NOT NULL,
        password_hash character varying(512) NOT NULL,
        security_stamp uuid NOT NULL,
        password_changed_at_utc timestamp with time zone NOT NULL,
        is_active boolean NOT NULL,
        failed_password_attempts integer NOT NULL,
        is_locked_out boolean NOT NULL,
        lockout_end_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_platform_users PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    CREATE TABLE identity.refresh_sessions (
        id uuid NOT NULL,
        family_id uuid NOT NULL,
        platform_user_id uuid NOT NULL,
        tenant_id uuid,
        kind character varying(40) NOT NULL,
        scope character varying(40) NOT NULL,
        refresh_token_hash bytea NOT NULL,
        issued_at_utc timestamp with time zone NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        absolute_expires_at_utc timestamp with time zone NOT NULL,
        rotated_at_utc timestamp with time zone,
        successor_id uuid,
        is_revoked boolean NOT NULL,
        revoked_at_utc timestamp with time zone,
        revocation_reason character varying(40),
        last_seen_at_utc timestamp with time zone NOT NULL,
        reauthenticated_at_utc timestamp with time zone,
        ip_address inet,
        user_agent character varying(512),
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_refresh_sessions PRIMARY KEY (id),
        CONSTRAINT ck_refresh_sessions_kind_domain CHECK ("kind" IN ('Browser', 'Machine')),
        CONSTRAINT ck_refresh_sessions_revocation_reason_domain CHECK ("revocation_reason" IN ('Logout', 'Admin', 'PasswordChanged', 'ReuseDetected', 'Expired', 'TenantSuspended')),
        CONSTRAINT ck_refresh_sessions_scope_domain CHECK ("scope" IN ('Full', 'MfaPending', 'MfaEnrollment')),
        CONSTRAINT fk_refresh_sessions_platform_users_platform_user_id FOREIGN KEY (platform_user_id) REFERENCES identity.platform_users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    CREATE TABLE app.tenant_memberships (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        platform_user_id uuid NOT NULL,
        employee_code character varying(32) NOT NULL,
        full_name_en character varying(400) NOT NULL,
        full_name_ar character varying(400) NOT NULL,
        is_active boolean NOT NULL,
        branch_ids uuid[] NOT NULL,
        department_ids uuid[] NOT NULL,
        preferred_language character varying(5) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_tenant_memberships PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_tenant_memberships_platform_users_platform_user_id FOREIGN KEY (platform_user_id) REFERENCES identity.platform_users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    ALTER TABLE app.tenant_memberships ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.tenant_memberships FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.tenant_memberships
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    CREATE INDEX ix_outbox_events_pending ON ops.outbox_events (next_attempt_at_utc) WHERE processed_at_utc IS NULL AND dead_lettered_at_utc IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    CREATE UNIQUE INDEX ix_platform_users_email ON identity.platform_users (email);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    CREATE UNIQUE INDEX ix_platform_users_username ON identity.platform_users (username);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    CREATE INDEX ix_refresh_sessions_family_id ON identity.refresh_sessions (family_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    CREATE UNIQUE INDEX ix_refresh_sessions_refresh_token_hash ON identity.refresh_sessions (refresh_token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    CREATE INDEX ix_refresh_sessions_user_live ON identity.refresh_sessions (platform_user_id) WHERE is_revoked = false;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    CREATE INDEX ix_tenant_memberships_platform_user_id ON app.tenant_memberships (platform_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    CREATE UNIQUE INDEX ix_tenant_memberships_tenant_id_employee_code ON app.tenant_memberships (tenant_id, employee_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    CREATE UNIQUE INDEX ix_tenant_memberships_tenant_id_platform_user_id ON app.tenant_memberships (tenant_id, platform_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008223259_InitialIdentityAndTenancy') THEN
    INSERT INTO ops.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261008223259_InitialIdentityAndTenancy', '9.0.20');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008233351_SecurityStores') THEN
    CREATE UNLOGGED TABLE app.credential_nonces (
        nonce_hash bytea NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_credential_nonces PRIMARY KEY (nonce_hash)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008233351_SecurityStores') THEN
    CREATE TABLE identity.envelope_keys (
        kid character varying(64) NOT NULL,
        public_key_pem text NOT NULL,
        private_key_enc bytea NOT NULL,
        nonce_mac_key_enc bytea NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        retired_at_utc timestamp with time zone,
        CONSTRAINT pk_envelope_keys PRIMARY KEY (kid)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008233351_SecurityStores') THEN
    CREATE INDEX ix_credential_nonces_expiry ON app.credential_nonces (expires_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008233351_SecurityStores') THEN
    CREATE INDEX ix_envelope_keys_created ON identity.envelope_keys (created_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261008233351_SecurityStores') THEN
    INSERT INTO ops.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261008233351_SecurityStores', '9.0.20');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    ALTER TABLE app.tenant_memberships ADD role_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    CREATE TABLE app.reference_counters (
        tenant_id uuid NOT NULL,
        kind character varying(40) NOT NULL,
        year integer NOT NULL,
        last_value bigint NOT NULL,
        CONSTRAINT pk_reference_counters PRIMARY KEY (tenant_id, kind, year)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    CREATE TABLE app.roles (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name_en character varying(400) NOT NULL,
        name_ar character varying(400) NOT NULL,
        permission_keys text[] NOT NULL,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_roles PRIMARY KEY (tenant_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    CREATE TABLE app.treasuries (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(16) NOT NULL,
        name_en character varying(400) NOT NULL,
        name_ar character varying(400) NOT NULL,
        branch_id uuid NOT NULL,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_treasuries PRIMARY KEY (tenant_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    CREATE TABLE app.treasury_access_requests (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        number character varying(20) NOT NULL,
        treasury_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        requested_by_user_id uuid NOT NULL,
        device_id character varying(128) NOT NULL,
        device_label character varying(100),
        status character varying(40) NOT NULL,
        requested_at_utc timestamp with time zone NOT NULL,
        approved_by_user_id uuid,
        approved_at_utc timestamp with time zone,
        approval_comment character varying(1000),
        decided_by_user_id uuid,
        decided_at_utc timestamp with time zone,
        decision_reason character varying(1000),
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_treasury_access_requests PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_treasury_access_requests_status_domain CHECK ("status" IN ('Pending', 'Approved', 'Rejected', 'Revoked', 'Cancelled')),
        CONSTRAINT fk_treasury_access_requests_requested_by_user FOREIGN KEY (requested_by_user_id) REFERENCES identity.platform_users (id) ON DELETE RESTRICT,
        CONSTRAINT fk_treasury_access_requests_treasuries_tenant_id_treasury_id FOREIGN KEY (tenant_id, treasury_id) REFERENCES app.treasuries (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    CREATE INDEX ix_tenant_memberships_tenant_id_role_id ON app.tenant_memberships (tenant_id, role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    CREATE INDEX ix_roles_tenant_id_is_active ON app.roles (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    CREATE INDEX ix_treasuries_tenant_id_branch_id ON app.treasuries (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    CREATE UNIQUE INDEX ix_treasuries_tenant_id_code ON app.treasuries (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    CREATE INDEX ix_treasury_access_requests_requested_by_user_id ON app.treasury_access_requests (requested_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    CREATE UNIQUE INDEX ix_treasury_access_requests_tenant_id_number ON app.treasury_access_requests (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    CREATE INDEX ix_treasury_access_requests_tenant_id_requested_by_user_id ON app.treasury_access_requests (tenant_id, requested_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    CREATE INDEX ix_treasury_access_requests_tenant_id_status ON app.treasury_access_requests (tenant_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    CREATE INDEX ix_treasury_access_requests_tenant_id_treasury_id ON app.treasury_access_requests (tenant_id, treasury_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    CREATE UNIQUE INDEX ux_treasury_access_requests_live ON app.treasury_access_requests (tenant_id, requested_by_user_id, treasury_id, device_id) WHERE status IN ('Pending', 'Approved');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    ALTER TABLE app.tenant_memberships ADD CONSTRAINT fk_tenant_memberships_roles_tenant_id_role_id FOREIGN KEY (tenant_id, role_id) REFERENCES app.roles (tenant_id, id) ON DELETE RESTRICT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    ALTER TABLE app.roles ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.roles FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.roles
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.treasuries ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.treasuries FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.treasuries
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.treasury_access_requests ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.treasury_access_requests FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.treasury_access_requests
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.reference_counters ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.reference_counters FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.reference_counters
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009083855_TreasuryModule') THEN
    INSERT INTO ops.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261009083855_TreasuryModule', '9.0.20');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'audit') THEN
            CREATE SCHEMA audit;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    ALTER TABLE identity.refresh_sessions ADD branch_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    ALTER TABLE identity.platform_users ADD mobile character varying(20);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE TABLE audit.audit_trail (
        tenant_id uuid NOT NULL,
        sequence bigint NOT NULL,
        event_id uuid NOT NULL,
        event_type character varying(400) NOT NULL,
        aggregate_type character varying(200) NOT NULL,
        aggregate_id character varying(200) NOT NULL,
        actor_user_id uuid,
        at_utc timestamp with time zone NOT NULL,
        correlation_id character varying(128),
        payload jsonb NOT NULL,
        prev_hash bytea,
        entry_hash bytea NOT NULL,
        CONSTRAINT pk_audit_trail PRIMARY KEY (tenant_id, sequence)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE TABLE audit.field_changes (
        tenant_id uuid NOT NULL,
        id uuid NOT NULL,
        entity_type character varying(200) NOT NULL,
        entity_id character varying(200) NOT NULL,
        action character varying(40) NOT NULL,
        actor_user_id uuid,
        actor_name character varying(200),
        at_utc timestamp with time zone NOT NULL,
        correlation_id character varying(128),
        changes jsonb NOT NULL,
        reason character varying(1000),
        CONSTRAINT pk_field_changes PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_field_changes_action_domain CHECK ("action" IN ('Created', 'Modified', 'Deleted'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE TABLE identity.otp_challenges (
        id uuid NOT NULL,
        purpose character varying(40) NOT NULL,
        target_hash bytea NOT NULL,
        code_hash bytea NOT NULL,
        issued_at_utc timestamp with time zone NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        attempts integer NOT NULL,
        consumed_at_utc timestamp with time zone,
        CONSTRAINT pk_otp_challenges PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE TABLE audit.security_events (
        id uuid NOT NULL,
        tenant_id uuid,
        name character varying(64) NOT NULL,
        severity character varying(16) NOT NULL,
        actor_user_id uuid,
        client_ip inet,
        user_agent_hash bytea,
        correlation_id character varying(128),
        detail character varying(1000),
        at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_security_events PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE TABLE app.tenant_settings (
        tenant_id uuid NOT NULL,
        key character varying(80) NOT NULL,
        value character varying(200) NOT NULL,
        updated_at_utc timestamp with time zone NOT NULL,
        updated_by_user_id uuid,
        CONSTRAINT pk_tenant_settings PRIMARY KEY (tenant_id, key)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE UNIQUE INDEX ux_platform_users_mobile ON identity.platform_users (mobile) WHERE mobile IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE INDEX ix_audit_trail_tenant_aggregate ON audit.audit_trail (tenant_id, aggregate_type, aggregate_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE INDEX ix_audit_trail_tenant_time ON audit.audit_trail (tenant_id, at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE UNIQUE INDEX ux_audit_trail_tenant_event ON audit.audit_trail (tenant_id, event_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE INDEX ix_field_changes_tenant_actor ON audit.field_changes (tenant_id, actor_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE INDEX ix_field_changes_tenant_entity ON audit.field_changes (tenant_id, entity_type, entity_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE INDEX ix_field_changes_tenant_time ON audit.field_changes (tenant_id, at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE INDEX ix_otp_challenges_live ON identity.otp_challenges (purpose, target_hash) WHERE consumed_at_utc IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE INDEX ix_security_events_actor_time ON audit.security_events (actor_user_id, at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE INDEX ix_security_events_tenant_time ON audit.security_events (tenant_id, at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    CREATE OR REPLACE FUNCTION audit.refuse_change() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN
        RAISE EXCEPTION 'audit rows are append-only: % on %.% is not allowed', TG_OP, TG_TABLE_SCHEMA, TG_TABLE_NAME;
    END
    $fn$;
    CREATE TRIGGER field_changes_append_only BEFORE UPDATE OR DELETE ON audit.field_changes FOR EACH ROW EXECUTE FUNCTION audit.refuse_change();
    CREATE TRIGGER audit_trail_append_only BEFORE UPDATE OR DELETE ON audit.audit_trail FOR EACH ROW EXECUTE FUNCTION audit.refuse_change();
    CREATE TRIGGER security_events_append_only BEFORE UPDATE OR DELETE ON audit.security_events FOR EACH ROW EXECUTE FUNCTION audit.refuse_change();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    ALTER TABLE audit.security_events ENABLE ROW LEVEL SECURITY;
    ALTER TABLE audit.security_events FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON audit.security_events
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id IS NULL
            OR tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR NULLIF(current_setting('app.current_tenant', true), '')::uuid = '00000000-0000-0000-0000-000000000000'::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    ALTER TABLE audit.audit_trail ENABLE ROW LEVEL SECURITY;
    ALTER TABLE audit.audit_trail FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON audit.audit_trail
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE audit.field_changes ENABLE ROW LEVEL SECURITY;
    ALTER TABLE audit.field_changes FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON audit.field_changes
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.tenant_settings ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.tenant_settings FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.tenant_settings
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261009100252_Foundation') THEN
    INSERT INTO ops.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261009100252_Foundation', '9.0.20');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
        IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'saas') THEN
            CREATE SCHEMA saas;
        END IF;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD added_permission_keys text[] NOT NULL DEFAULT ARRAY[]::text[];
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD designation character varying(100);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD is_primary_administrator boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD override_can_cancel boolean;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD override_can_modify_verified_result boolean;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD override_can_pay boolean;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD override_can_preview_without_verify boolean;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD override_can_print_invoice boolean;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD override_can_refund boolean;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD override_can_see_vip boolean;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD override_can_transfer boolean;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD override_discount_cap_percent integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD removed_permission_keys text[] NOT NULL DEFAULT ARRAY[]::text[];
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD shifts text[] NOT NULL DEFAULT ARRAY[]::text[];
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.tenant_memberships ADD signature_file_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.roles ADD description character varying(500);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.roles ADD special_can_cancel boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.roles ADD special_can_modify_verified_result boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.roles ADD special_can_pay boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.roles ADD special_can_preview_without_verify boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.roles ADD special_can_print_invoice boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.roles ADD special_can_refund boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.roles ADD special_can_see_vip boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.roles ADD special_can_transfer boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.roles ADD special_discount_cap_percent integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE identity.platform_users ADD must_change_password boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE identity.platform_users ADD password_expires_at_utc timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.agent_log_entries (
        tenant_id uuid NOT NULL,
        id uuid NOT NULL,
        agent_id uuid NOT NULL,
        at_utc timestamp with time zone NOT NULL,
        level character varying(40) NOT NULL,
        message character varying(2000) NOT NULL,
        sample_number character varying(64),
        analyser_code character varying(16),
        received_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_agent_log_entries PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_agent_log_entries_level_domain CHECK ("level" IN ('Debug', 'Info', 'Warning', 'Error'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.analyser_connection_states (
        tenant_id uuid NOT NULL,
        id uuid NOT NULL,
        site_agent_id uuid NOT NULL,
        status character varying(40) NOT NULL,
        message character varying(500),
        last_seen_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_analyser_connection_states PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_analyser_connection_states_status_domain CHECK ("status" IN ('Unknown', 'Connected', 'Disconnected', 'Error'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.analyser_models (
        id uuid NOT NULL,
        vendor character varying(100) NOT NULL,
        model character varying(100) NOT NULL,
        discipline character varying(100) NOT NULL,
        protocol character varying(40) NOT NULL,
        physical_link character varying(40) NOT NULL,
        modes text[] NOT NULL,
        driver_version character varying(40),
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_analyser_models PRIMARY KEY (id),
        CONSTRAINT ck_analyser_models_physical_link_domain CHECK ("physical_link" IN ('Serial', 'Tcp', 'File')),
        CONSTRAINT ck_analyser_models_protocol_domain CHECK ("protocol" IN ('Astm', 'Hl7', 'Proprietary'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.analyser_result_intake (
        tenant_id uuid NOT NULL,
        id uuid NOT NULL,
        agent_id uuid NOT NULL,
        analyser_id uuid,
        analyser_code character varying(16) NOT NULL,
        sample_number character varying(64) NOT NULL,
        analyser_test_code character varying(32) NOT NULL,
        test_id uuid,
        mapping_id uuid,
        raw_value character varying(500) NOT NULL,
        converted_value character varying(500),
        flags text[] NOT NULL,
        status character varying(40) NOT NULL,
        reason character varying(500),
        source character varying(16) NOT NULL,
        file_name character varying(260),
        measured_at_utc timestamp with time zone,
        at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_analyser_result_intake PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_analyser_result_intake_status_domain CHECK ("status" IN ('Accepted', 'Refused', 'Unmapped', 'AutoSendOff', 'UnknownSample'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.analysers (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(16) NOT NULL,
        name_en character varying(400) NOT NULL,
        name_ar character varying(400) NOT NULL,
        clinical_group_id uuid NOT NULL,
        default_sample_type_id uuid,
        analyser_model_id uuid,
        mode character varying(40) NOT NULL,
        connection_kind character varying(40) NOT NULL,
        serial_settings character varying(20),
        serial_port character varying(64),
        tcp_host character varying(253),
        tcp_port integer,
        tcp_role character varying(40),
        folder_path character varying(260),
        auto_print boolean NOT NULL,
        download_orders boolean NOT NULL,
        append_unregistered_tests boolean NOT NULL,
        append_unregistered_samples boolean NOT NULL,
        overwrite_reviewed boolean NOT NULL,
        overwrite_verified boolean NOT NULL,
        print_copies integer NOT NULL,
        site_agent_id uuid,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_analysers PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_analysers_connection_kind_domain CHECK ("connection_kind" IN ('Serial', 'Tcp', 'File')),
        CONSTRAINT ck_analysers_mode_domain CHECK ("mode" IN ('Unidirectional', 'Bidirectional', 'Download', 'SharedFolder')),
        CONSTRAINT ck_analysers_print_copies_range CHECK (print_copies >= 0 AND print_copies <= 10),
        CONSTRAINT ck_analysers_tcp_role_domain CHECK ("tcp_role" IS NULL OR "tcp_role" IN ('Client', 'Server'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.base_price_lists (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(16) NOT NULL,
        name_en character varying(400) NOT NULL,
        name_ar character varying(400) NOT NULL,
        year integer NOT NULL,
        is_active boolean NOT NULL,
        copied_from_list_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_base_price_lists PRIMARY KEY (tenant_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.branches (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(16) NOT NULL,
        name_en character varying(400) NOT NULL,
        name_ar character varying(400) NOT NULL,
        country_id uuid,
        governorate_id uuid,
        phone character varying(20),
        email character varying(254),
        currency character varying(3) NOT NULL,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_branches PRIMARY KEY (tenant_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.cash_closings (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        number character varying(20) NOT NULL,
        treasury_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        cashier_user_id uuid,
        period_from_utc timestamp with time zone NOT NULL,
        period_to_utc timestamp with time zone NOT NULL,
        cash_payments numeric(14,2) NOT NULL,
        card_payments numeric(14,2) NOT NULL,
        pos_payments numeric(14,2) NOT NULL,
        transfer_payments numeric(14,2) NOT NULL,
        payment_count integer NOT NULL,
        cash_refunds numeric(14,2) NOT NULL,
        card_refunds numeric(14,2) NOT NULL,
        pos_refunds numeric(14,2) NOT NULL,
        transfer_refunds numeric(14,2) NOT NULL,
        refund_count integer NOT NULL,
        outstanding_balance numeric(14,2) NOT NULL,
        discount_total numeric(14,2) NOT NULL,
        visit_count integer NOT NULL,
        expected_cash numeric(14,2) NOT NULL,
        counted_cash numeric(14,2),
        difference numeric(14,2),
        note character varying(1000),
        closed_by_user_id uuid NOT NULL,
        closed_at_utc timestamp with time zone NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_cash_closings PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_cash_closings_period CHECK (period_from_utc < period_to_utc),
        CONSTRAINT fk_cash_closings_platform_users_cashier_user_id FOREIGN KEY (cashier_user_id) REFERENCES identity.platform_users (id) ON DELETE RESTRICT,
        CONSTRAINT fk_cash_closings_platform_users_closed_by_user_id FOREIGN KEY (closed_by_user_id) REFERENCES identity.platform_users (id) ON DELETE RESTRICT,
        CONSTRAINT fk_cash_closings_treasuries_tenant_id_treasury_id FOREIGN KEY (tenant_id, treasury_id) REFERENCES app.treasuries (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.clinical_groups (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(16) NOT NULL,
        unit_code character varying(4) NOT NULL,
        name_en character varying(400) NOT NULL,
        name_ar character varying(400) NOT NULL,
        report_name character varying(200),
        report_template_id uuid,
        display_order integer NOT NULL,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_clinical_groups PRIMARY KEY (tenant_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.code_systems (
        id uuid NOT NULL,
        name character varying(40) NOT NULL,
        version character varying(40) NOT NULL,
        use character varying(300),
        item_count integer NOT NULL,
        last_imported_at_utc timestamp with time zone,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_code_systems PRIMARY KEY (id),
        CONSTRAINT ck_code_systems_name_domain CHECK ("name" IN ('Loinc', 'Cpt', 'Icd10', 'Snomed'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.comment_templates (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        short_name character varying(32) NOT NULL,
        text_en character varying(400) NOT NULL,
        text_ar character varying(400) NOT NULL,
        clinical_group_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        is_active boolean NOT NULL,
        CONSTRAINT pk_comment_templates PRIMARY KEY (tenant_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.countries (
        id uuid NOT NULL,
        name_en character varying(100) NOT NULL,
        name_ar character varying(100) NOT NULL,
        iso_code character(2) NOT NULL,
        dial_code character varying(5) NOT NULL,
        currency character varying(60) NOT NULL,
        currency_code character(3) NOT NULL,
        language character varying(2) NOT NULL,
        time_zone character varying(64) NOT NULL,
        mobile_length integer NOT NULL,
        flag character varying(16),
        remarks character varying(500),
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_countries PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.diagnoses (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        icd_code character varying(20) NOT NULL,
        name_en character varying(200) NOT NULL,
        name_ar character varying(200) NOT NULL,
        service_ids uuid[] NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        is_active boolean NOT NULL,
        CONSTRAINT pk_diagnoses PRIMARY KEY (tenant_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.doctors (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name_en character varying(200) NOT NULL,
        name_ar character varying(200) NOT NULL,
        specialty character varying(100),
        occupation character varying(100),
        branch_ids uuid[] NOT NULL,
        share_percent numeric(5,2) NOT NULL,
        phone character varying(20),
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        is_active boolean NOT NULL,
        CONSTRAINT pk_doctors PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_doctors_share_percent_range CHECK (share_percent >= 0 AND share_percent <= 100)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.drug_types (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name_en character varying(200) NOT NULL,
        name_ar character varying(200) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        is_active boolean NOT NULL,
        CONSTRAINT pk_drug_types PRIMARY KEY (tenant_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE identity.machine_credentials (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        client_id character varying(36) NOT NULL,
        name character varying(100) NOT NULL,
        secret_hash character varying(128) NOT NULL,
        service_user_id uuid NOT NULL,
        scopes text[] NOT NULL,
        branch_id uuid,
        expires_at_utc timestamp with time zone NOT NULL,
        secret_rotated_at_utc timestamp with time zone,
        last_used_at_utc timestamp with time zone,
        revoked_at_utc timestamp with time zone,
        revoked_by_user_id uuid,
        revocation_reason character varying(500),
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_machine_credentials PRIMARY KEY (id),
        CONSTRAINT fk_machine_credentials_service_user FOREIGN KEY (service_user_id) REFERENCES identity.platform_users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.measurement_units (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name character varying(50) NOT NULL,
        name_key character varying(50) NOT NULL,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_measurement_units PRIMARY KEY (tenant_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.notification_templates (
        id uuid NOT NULL,
        code character varying(40) NOT NULL,
        channel character varying(40) NOT NULL,
        subject_en character varying(200),
        subject_ar character varying(200),
        text_en character varying(2000) NOT NULL,
        text_ar character varying(2000) NOT NULL,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_notification_templates PRIMARY KEY (id),
        CONSTRAINT ck_notification_templates_channel_domain CHECK ("channel" IN ('Sms', 'Email'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.organisms (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name_en character varying(200) NOT NULL,
        name_ar character varying(200) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        is_active boolean NOT NULL,
        CONSTRAINT pk_organisms PRIMARY KEY (tenant_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.patient_conditions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name_en character varying(400) NOT NULL,
        name_ar character varying(400) NOT NULL,
        name_key character varying(400) NOT NULL,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_patient_conditions PRIMARY KEY (tenant_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.patients (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        number character varying(20) NOT NULL,
        title character varying(40) NOT NULL,
        full_name character varying(200) NOT NULL,
        name_ar character varying(200),
        name_key character varying(400) NOT NULL,
        name_ar_key character varying(400) NOT NULL,
        gender character varying(40) NOT NULL,
        date_of_birth date,
        age_years integer,
        age_months integer,
        age_days integer,
        age_recorded_on date,
        registered_on date NOT NULL,
        patient_condition_id uuid,
        mobile character varying(16),
        national_id character varying(32),
        nationality character varying(64),
        email character varying(254),
        governorate_id uuid,
        city character varying(100),
        address character varying(500),
        comments text,
        is_vip boolean NOT NULL,
        status character varying(40) NOT NULL,
        merged_into_patient_id uuid,
        merged_at_utc timestamp with time zone,
        merged_by_user_id uuid,
        merge_reason character varying(1000),
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_patients PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_patients_birth_or_age CHECK (date_of_birth IS NOT NULL OR (age_years IS NOT NULL AND age_recorded_on IS NOT NULL)),
        CONSTRAINT ck_patients_gender_domain CHECK ("gender" IN ('Male', 'Female')),
        CONSTRAINT ck_patients_merge_consistency CHECK ((status = 'Merged') = (merged_into_patient_id IS NOT NULL AND merged_at_utc IS NOT NULL AND merged_by_user_id IS NOT NULL AND merge_reason IS NOT NULL)),
        CONSTRAINT ck_patients_not_merged_into_self CHECK (merged_into_patient_id IS NULL OR merged_into_patient_id <> id),
        CONSTRAINT ck_patients_status_domain CHECK ("status" IN ('Active', 'Merged')),
        CONSTRAINT ck_patients_title_domain CHECK ("title" IN ('Mr', 'Mrs', 'Miss', 'Ms', 'Dr', 'Master', 'Baby', 'Prof')),
        CONSTRAINT fk_patients_patients_tenant_id_merged_into_patient_id FOREIGN KEY (tenant_id, merged_into_patient_id) REFERENCES app.patients (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.payers (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name_en character varying(400) NOT NULL,
        name_ar character varying(400) NOT NULL,
        search_key character varying(32) NOT NULL,
        type character varying(40) NOT NULL,
        web_portal_enabled boolean NOT NULL,
        web_username character varying(64),
        is_active boolean NOT NULL,
        is_default boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_payers PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_payers_type_domain CHECK ("type" IN ('Cash', 'Credit', 'Insurance', 'Lab'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.plans (
        id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name_en character varying(100) NOT NULL,
        name_ar character varying(100) NOT NULL,
        monthly_fee numeric(12,2) NOT NULL,
        currency_code character(3) NOT NULL,
        max_branches integer NOT NULL,
        max_users integer NOT NULL,
        sms_quota integer NOT NULL,
        max_analysers integer NOT NULL,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_plans PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.preparations (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(20) NOT NULL,
        question_en character varying(400) NOT NULL,
        question_ar character varying(400) NOT NULL,
        answer_type character varying(40) NOT NULL,
        options text[] NOT NULL,
        gender character varying(40) NOT NULL,
        service_ids uuid[] NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        is_active boolean NOT NULL,
        CONSTRAINT pk_preparations PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_preparations_answer_type_domain CHECK ("answer_type" IN ('Text', 'YesNo', 'OptionList')),
        CONSTRAINT ck_preparations_gender_domain CHECK ("gender" IN ('Any', 'Male', 'Female'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.reasons (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        kind character varying(40) NOT NULL,
        name_en character varying(200) NOT NULL,
        name_ar character varying(200) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        is_active boolean NOT NULL,
        CONSTRAINT pk_reasons PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_reasons_kind_domain CHECK ("kind" IN ('Rejection', 'Cancellation', 'Discount', 'Refund', 'Transfer'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.report_templates (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name_en character varying(200) NOT NULL,
        name_ar character varying(200) NOT NULL,
        type character varying(40) NOT NULL,
        logo_file_id uuid,
        signature_file_id uuid,
        show_qr boolean NOT NULL,
        language character varying(40) NOT NULL,
        header_text_en character varying(400),
        header_text_ar character varying(400),
        footer_text_en character varying(400),
        footer_text_ar character varying(400),
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        is_active boolean NOT NULL,
        CONSTRAINT pk_report_templates PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_report_templates_language_domain CHECK ("language" IN ('En', 'Ar', 'Both')),
        CONSTRAINT ck_report_templates_type_domain CHECK ("type" IN ('Results', 'Culture', 'Text', 'Claim', 'Receipt', 'Label', 'JobOrder'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.role_result_rights (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        clinical_group_id uuid NOT NULL,
        branch_id uuid,
        can_see boolean NOT NULL,
        can_review boolean NOT NULL,
        can_verify boolean NOT NULL,
        can_edit boolean NOT NULL,
        can_print boolean NOT NULL,
        role_id uuid NOT NULL,
        CONSTRAINT "PK_role_result_rights" PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_role_result_rights_roles_tenant_id_role_id FOREIGN KEY (tenant_id, role_id) REFERENCES app.roles (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.rule_templates (
        id uuid NOT NULL,
        name character varying(150) NOT NULL,
        clinical_group_name character varying(100),
        formula character varying(2000) NOT NULL,
        description character varying(1000),
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_rule_templates PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.sample_types (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(20) NOT NULL,
        short_name character varying(20) NOT NULL,
        name_en character varying(400) NOT NULL,
        name_ar character varying(400) NOT NULL,
        is_active boolean NOT NULL,
        revision integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_sample_types PRIMARY KEY (tenant_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.samples (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        tube_id uuid NOT NULL,
        generation integer NOT NULL,
        sample_number character varying(32) NOT NULL,
        visit_id uuid NOT NULL,
        accession character varying(40) NOT NULL,
        patient_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        clinical_group_id uuid NOT NULL,
        sample_type_id uuid NOT NULL,
        sample_condition_id uuid,
        service_ids uuid[] NOT NULL,
        status character varying(40) NOT NULL,
        ordered_at_utc timestamp with time zone NOT NULL,
        status_changed_at_utc timestamp with time zone NOT NULL,
        collected_at_utc timestamp with time zone,
        collected_by_user_id uuid,
        collected_branch_id uuid,
        received_at_utc timestamp with time zone,
        received_by_user_id uuid,
        received_branch_id uuid,
        rejection_reason_id uuid,
        rejection_reason_name_en character varying(400),
        rejection_reason_name_ar character varying(400),
        rejection_note character varying(500),
        rejected_at_utc timestamp with time zone,
        rejected_by_user_id uuid,
        rejected_branch_id uuid,
        replaced_by_sample_id uuid,
        replaces_sample_id uuid,
        cancelled_at_utc timestamp with time zone,
        cancelled_by_user_id uuid,
        cancellation_reason character varying(1000),
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_samples PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_samples_status_domain CHECK ("status" IN ('Ordered', 'Collected', 'Received', 'Rejected', 'Cancelled', 'Replaced'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.sensitivity_values (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(10) NOT NULL,
        name_en character varying(200) NOT NULL,
        name_ar character varying(200) NOT NULL,
        display_order integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        is_active boolean NOT NULL,
        CONSTRAINT pk_sensitivity_values PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_sensitivity_values_display_order_range CHECK (display_order >= 0 AND display_order <= 999)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.shortcut_sets (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name_en character varying(200) NOT NULL,
        name_ar character varying(200) NOT NULL,
        service_ids uuid[] NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        is_active boolean NOT NULL,
        CONSTRAINT pk_shortcut_sets PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_shortcut_sets_services_present CHECK (cardinality(service_ids) >= 1)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.site_agent_states (
        tenant_id uuid NOT NULL,
        id uuid NOT NULL,
        version character varying(64),
        reported_error boolean NOT NULL,
        message character varying(500),
        last_heartbeat_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_site_agent_states PRIMARY KEY (tenant_id, id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.subscription_requests (
        id uuid NOT NULL,
        name_en character varying(200) NOT NULL,
        name_ar character varying(200) NOT NULL,
        country_id uuid NOT NULL,
        governorate_id uuid NOT NULL,
        address character varying(300) NOT NULL,
        mobile character varying(20) NOT NULL,
        licence_no character varying(64),
        manager_name character varying(200) NOT NULL,
        manager_mobile character varying(20) NOT NULL,
        email character varying(254),
        language character varying(5) NOT NULL,
        status character varying(40) NOT NULL,
        started_at_utc timestamp with time zone NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        codes_sent integer NOT NULL,
        last_code_sent_at_utc timestamp with time zone NOT NULL,
        verified_at_utc timestamp with time zone,
        completed_at_utc timestamp with time zone,
        tenant_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_subscription_requests PRIMARY KEY (id),
        CONSTRAINT ck_subscription_requests_status_domain CHECK ("status" IN ('Pending', 'Verified', 'Completed'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.support_tickets (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        number character varying(20) NOT NULL,
        subject character varying(200) NOT NULL,
        description character varying(4000) NOT NULL,
        priority character varying(40) NOT NULL,
        status character varying(40) NOT NULL,
        opened_by_user_id uuid NOT NULL,
        opened_by_name character varying(200) NOT NULL,
        laboratory_name_en character varying(200),
        laboratory_name_ar character varying(200),
        opened_at_utc timestamp with time zone NOT NULL,
        last_activity_at_utc timestamp with time zone NOT NULL,
        resolved_at_utc timestamp with time zone,
        closed_at_utc timestamp with time zone,
        closed_by_user_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_support_tickets PRIMARY KEY (id),
        CONSTRAINT ck_support_tickets_priority_domain CHECK ("priority" IN ('Low', 'Normal', 'High')),
        CONSTRAINT ck_support_tickets_status_domain CHECK ("status" IN ('Open', 'InProgress', 'Resolved', 'Closed')),
        CONSTRAINT fk_support_tickets_opened_by_user FOREIGN KEY (opened_by_user_id) REFERENCES identity.platform_users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.test_rules (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name_en character varying(200) NOT NULL,
        name_ar character varying(200) NOT NULL,
        clinical_group_id uuid,
        trigger_service_ids uuid[] NOT NULL,
        added_service_ids uuid[] NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        is_active boolean NOT NULL,
        CONSTRAINT pk_test_rules PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_test_rules_sides CHECK (cardinality(trigger_service_ids) >= 1 AND cardinality(added_service_ids) >= 1 AND NOT (trigger_service_ids && added_service_ids))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.visit_accounts (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        paid_total numeric(14,2) NOT NULL,
        refunded_total numeric(14,2) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_visit_accounts PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_visit_accounts_totals CHECK (paid_total >= 0 AND refunded_total >= 0)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.pcr_kit_templates (
        id uuid NOT NULL,
        name character varying(150) NOT NULL,
        analyser_model_id uuid,
        layout_json jsonb NOT NULL,
        thresholds_json jsonb NOT NULL,
        verdict_rules_json jsonb NOT NULL,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_pcr_kit_templates PRIMARY KEY (id),
        CONSTRAINT fk_pcr_kit_templates_analyser_models_analyser_model_id FOREIGN KEY (analyser_model_id) REFERENCES saas.analyser_models (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.analyser_flags (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(32) NOT NULL,
        action character varying(40) NOT NULL,
        description character varying(200),
        analyser_id uuid NOT NULL,
        CONSTRAINT "PK_analyser_flags" PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_analyser_flags_action_domain CHECK ("action" IN ('Record', 'Stop')),
        CONSTRAINT fk_analyser_flags_analysers_tenant_id_analyser_id FOREIGN KEY (tenant_id, analyser_id) REFERENCES app.analysers (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.analyser_sample_codes (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        analyser_id uuid NOT NULL,
        sample_type_id uuid NOT NULL,
        code character varying(16) NOT NULL,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_analyser_sample_codes PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_analyser_sample_codes_analyser FOREIGN KEY (tenant_id, analyser_id) REFERENCES app.analysers (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.analyser_test_mappings (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        analyser_id uuid NOT NULL,
        test_id uuid NOT NULL,
        analyser_code character varying(32) NOT NULL,
        host_code character varying(32),
        result_selection integer NOT NULL,
        slope numeric(18,8) NOT NULL,
        intercept numeric(18,8) NOT NULL,
        unit_id uuid,
        query boolean NOT NULL,
        query_details character varying(500),
        auto_send boolean NOT NULL,
        range_low numeric(18,6),
        range_high numeric(18,6),
        range_text character varying(200),
        methodology character varying(500),
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_analyser_test_mappings PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_analyser_test_mappings_slope_non_zero CHECK (slope <> 0),
        CONSTRAINT fk_analyser_test_mappings_analyser FOREIGN KEY (tenant_id, analyser_id) REFERENCES app.analysers (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.pcr_kits (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name character varying(100) NOT NULL,
        analyser_id uuid NOT NULL,
        analyser_test_code character varying(32) NOT NULL,
        serial_lock character varying(64),
        layout_delimiter character varying(1) NOT NULL,
        layout_sample_column character varying(64) NOT NULL,
        layout_gene_column character varying(64) NOT NULL,
        layout_well_column character varying(64),
        layout_content_column character varying(64),
        layout_patient_content_value character varying(64),
        layout_serial_label character varying(100),
        layout_no_amplification_tokens text[] NOT NULL,
        value_column character varying(64) NOT NULL,
        detected_min_positive_targets integer NOT NULL,
        detected_text character varying(100) NOT NULL,
        not_detected_text character varying(100) NOT NULL,
        presumptive_text character varying(100) NOT NULL,
        invalid_text character varying(100) NOT NULL,
        result_folder character varying(260) NOT NULL,
        backup_folder character varying(260) NOT NULL,
        failed_folder character varying(260) NOT NULL,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_pcr_kits PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_pcr_kits_detected_min_targets_range CHECK (detected_min_positive_targets >= 1 AND detected_min_positive_targets <= 10),
        CONSTRAINT fk_pcr_kits_analyser FOREIGN KEY (tenant_id, analyser_id) REFERENCES app.analysers (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.base_price_lines (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        price_list_id uuid NOT NULL,
        service_id uuid NOT NULL,
        price numeric(12,2) NOT NULL,
        is_active boolean NOT NULL,
        CONSTRAINT pk_base_price_lines PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_base_price_lines_list FOREIGN KEY (tenant_id, price_list_id) REFERENCES app.base_price_lists (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.code_system_items (
        code_system_id uuid NOT NULL,
        code character varying(64) NOT NULL,
        display character varying(1000) NOT NULL,
        CONSTRAINT pk_code_system_items PRIMARY KEY (code_system_id, code),
        CONSTRAINT fk_code_system_items_code_systems_code_system_id FOREIGN KEY (code_system_id) REFERENCES saas.code_systems (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.governorates (
        id uuid NOT NULL,
        country_id uuid NOT NULL,
        name_en character varying(100) NOT NULL,
        name_ar character varying(100) NOT NULL,
        district character varying(100),
        remarks character varying(500),
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_governorates PRIMARY KEY (id),
        CONSTRAINT fk_governorates_countries_country_id FOREIGN KEY (country_id) REFERENCES saas.countries (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.sending_labs (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name_en character varying(200) NOT NULL,
        name_ar character varying(200) NOT NULL,
        payer_id uuid NOT NULL,
        contract_tier_id uuid NOT NULL,
        courier_en character varying(200) NOT NULL,
        courier_ar character varying(200) NOT NULL,
        phone character varying(20),
        doctor_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        is_active boolean NOT NULL,
        CONSTRAINT pk_sending_labs PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_sending_labs_doctors_tenant_id_doctor_id FOREIGN KEY (tenant_id, doctor_id) REFERENCES app.doctors (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.commercial_names (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        drug_type_id uuid NOT NULL,
        name_en character varying(200) NOT NULL,
        name_ar character varying(200) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        is_active boolean NOT NULL,
        CONSTRAINT pk_commercial_names PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_commercial_names_drug_types_tenant_id_drug_type_id FOREIGN KEY (tenant_id, drug_type_id) REFERENCES app.drug_types (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.site_agents (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        name character varying(100) NOT NULL,
        branch_id uuid NOT NULL,
        machine_credential_id uuid NOT NULL,
        service_user_id uuid NOT NULL,
        config_json jsonb NOT NULL,
        config_version integer NOT NULL,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_site_agents PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_site_agents_config_version_positive CHECK (config_version >= 1),
        CONSTRAINT fk_site_agents_machine_credential FOREIGN KEY (machine_credential_id) REFERENCES identity.machine_credentials (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.patient_documents (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        patient_id uuid NOT NULL,
        file_name character varying(255) NOT NULL,
        content_type character varying(100) NOT NULL,
        size_bytes bigint NOT NULL,
        storage_key character varying(400) NOT NULL,
        sha256 character(64) NOT NULL,
        note character varying(500),
        uploaded_by_user_id uuid NOT NULL,
        uploaded_at_utc timestamp with time zone NOT NULL,
        is_active boolean NOT NULL,
        deactivated_by_user_id uuid,
        deactivated_at_utc timestamp with time zone,
        deactivation_reason character varying(1000),
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_patient_documents PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_patient_documents_deactivation CHECK (is_active OR (deactivated_at_utc IS NOT NULL AND deactivated_by_user_id IS NOT NULL AND deactivation_reason IS NOT NULL)),
        CONSTRAINT fk_patient_documents_patients_tenant_id_patient_id FOREIGN KEY (tenant_id, patient_id) REFERENCES app.patients (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.contract_tiers (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        payer_id uuid NOT NULL,
        name_en character varying(400) NOT NULL,
        name_ar character varying(400) NOT NULL,
        is_active boolean NOT NULL,
        is_default boolean NOT NULL,
        CONSTRAINT pk_contract_tiers PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_contract_tiers_payer FOREIGN KEY (tenant_id, payer_id) REFERENCES app.payers (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.sample_conditions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        sample_type_id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name_en character varying(400) NOT NULL,
        name_ar character varying(400) NOT NULL,
        is_active boolean NOT NULL,
        CONSTRAINT pk_sample_conditions PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_sample_conditions_sample_types_tenant_id_sample_type_id FOREIGN KEY (tenant_id, sample_type_id) REFERENCES app.sample_types (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.sample_events (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        sample_id uuid NOT NULL,
        action character varying(40) NOT NULL,
        status character varying(40) NOT NULL,
        at_utc timestamp with time zone NOT NULL,
        by_user_id uuid,
        branch_id uuid,
        reason_id uuid,
        note character varying(1000),
        CONSTRAINT pk_sample_events PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_sample_events_action_domain CHECK ("action" IN ('Ordered', 'Collected', 'Received', 'Rejected', 'Unrejected', 'Replaced', 'ReplacementOrdered', 'Cancelled')),
        CONSTRAINT ck_sample_events_status_domain CHECK ("status" IN ('Ordered', 'Collected', 'Received', 'Rejected', 'Cancelled', 'Replaced')),
        CONSTRAINT fk_sample_events_samples_tenant_id_sample_id FOREIGN KEY (tenant_id, sample_id) REFERENCES app.samples (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.support_ticket_replies (
        id uuid NOT NULL,
        author_user_id uuid NOT NULL,
        author_name character varying(200) NOT NULL,
        text character varying(4000) NOT NULL,
        from_platform boolean NOT NULL,
        at_utc timestamp with time zone NOT NULL,
        support_ticket_id uuid NOT NULL,
        CONSTRAINT pk_support_ticket_replies PRIMARY KEY (id),
        CONSTRAINT fk_support_ticket_replies_support_tickets_support_ticket_id FOREIGN KEY (support_ticket_id) REFERENCES saas.support_tickets (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.payments (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        visit_id uuid NOT NULL,
        receipt_number character varying(20) NOT NULL,
        amount numeric(14,2) NOT NULL,
        method character varying(40) NOT NULL,
        reference character varying(64),
        treasury_id uuid NOT NULL,
        device_id character varying(128) NOT NULL,
        cashier_user_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        batch_id uuid,
        paid_at_utc timestamp with time zone NOT NULL,
        status character varying(40) NOT NULL,
        void_reason character varying(500),
        voided_by_user_id uuid,
        voided_at_utc timestamp with time zone,
        CONSTRAINT pk_payments PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_payments_amount_positive CHECK (amount > 0),
        CONSTRAINT ck_payments_method_domain CHECK ("method" IN ('Cash', 'Card', 'Pos', 'Transfer')),
        CONSTRAINT ck_payments_reference_for_non_cash CHECK (method = 'Cash' OR reference IS NOT NULL),
        CONSTRAINT ck_payments_status_domain CHECK ("status" IN ('Posted', 'Voided')),
        CONSTRAINT ck_payments_void_consistency CHECK ((status = 'Voided') = (void_reason IS NOT NULL AND voided_at_utc IS NOT NULL AND voided_by_user_id IS NOT NULL)),
        CONSTRAINT fk_payments_platform_users_cashier_user_id FOREIGN KEY (cashier_user_id) REFERENCES identity.platform_users (id) ON DELETE RESTRICT,
        CONSTRAINT fk_payments_platform_users_voided_by_user_id FOREIGN KEY (voided_by_user_id) REFERENCES identity.platform_users (id) ON DELETE RESTRICT,
        CONSTRAINT fk_payments_treasuries_tenant_id_treasury_id FOREIGN KEY (tenant_id, treasury_id) REFERENCES app.treasuries (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_payments_visit_accounts_tenant_id_visit_id FOREIGN KEY (tenant_id, visit_id) REFERENCES app.visit_accounts (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.refunds (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        visit_id uuid NOT NULL,
        number character varying(20) NOT NULL,
        amount numeric(14,2) NOT NULL,
        method character varying(40) NOT NULL,
        reference character varying(64),
        reason_id uuid NOT NULL,
        note character varying(500),
        treasury_id uuid NOT NULL,
        device_id character varying(128) NOT NULL,
        cashier_user_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        refunded_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_refunds PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_refunds_amount_positive CHECK (amount > 0),
        CONSTRAINT ck_refunds_method_domain CHECK ("method" IN ('Cash', 'Card', 'Pos', 'Transfer')),
        CONSTRAINT ck_refunds_reference_for_non_cash CHECK (method = 'Cash' OR reference IS NOT NULL),
        CONSTRAINT fk_refunds_platform_users_cashier_user_id FOREIGN KEY (cashier_user_id) REFERENCES identity.platform_users (id) ON DELETE RESTRICT,
        CONSTRAINT fk_refunds_treasuries_tenant_id_treasury_id FOREIGN KEY (tenant_id, treasury_id) REFERENCES app.treasuries (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_refunds_visit_accounts_tenant_id_visit_id FOREIGN KEY (tenant_id, visit_id) REFERENCES app.visit_accounts (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.analyser_value_conversions (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        sort_order integer NOT NULL,
        from_value character varying(100) NOT NULL,
        to_value character varying(100) NOT NULL,
        mapping_id uuid NOT NULL,
        CONSTRAINT "PK_analyser_value_conversions" PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_analyser_value_conversions_analyser_test_mappings_tenant_id FOREIGN KEY (tenant_id, mapping_id) REFERENCES app.analyser_test_mappings (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.pcr_kit_genes (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        sort_order integer NOT NULL,
        name character varying(64) NOT NULL,
        is_internal_control boolean NOT NULL,
        positive_ct_max numeric(6,3) NOT NULL,
        borderline_ct_max numeric(6,3) NOT NULL,
        kit_id uuid NOT NULL,
        CONSTRAINT "PK_pcr_kit_genes" PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_pcr_kit_genes_thresholds CHECK (positive_ct_max > 0 AND positive_ct_max <= borderline_ct_max AND borderline_ct_max <= 60),
        CONSTRAINT fk_pcr_kit_genes_pcr_kits_tenant_id_kit_id FOREIGN KEY (tenant_id, kit_id) REFERENCES app.pcr_kits (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.tenants (
        id uuid NOT NULL,
        name_en character varying(200) NOT NULL,
        name_ar character varying(200) NOT NULL,
        country_id uuid NOT NULL,
        governorate_id uuid NOT NULL,
        address character varying(300) NOT NULL,
        mobile character varying(20) NOT NULL,
        licence_no character varying(64),
        manager_name character varying(200) NOT NULL,
        manager_mobile character varying(20) NOT NULL,
        email character varying(254),
        plan_id uuid NOT NULL,
        portal_slug character varying(40) NOT NULL,
        source character varying(40) NOT NULL,
        status character varying(40) NOT NULL,
        trial_ends_at_utc timestamp with time zone,
        activated_at_utc timestamp with time zone,
        deactivated_at_utc timestamp with time zone,
        deactivation_reason character varying(500),
        administrator_user_id uuid,
        administrator_username character varying(64),
        preferred_language character varying(5) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_tenants PRIMARY KEY (id),
        CONSTRAINT ck_tenants_source_domain CHECK ("source" IN ('Subscription', 'Manual')),
        CONSTRAINT ck_tenants_status_domain CHECK ("status" IN ('Trial', 'Active', 'Inactive')),
        CONSTRAINT fk_tenants_countries_country_id FOREIGN KEY (country_id) REFERENCES saas.countries (id) ON DELETE RESTRICT,
        CONSTRAINT fk_tenants_governorates_governorate_id FOREIGN KEY (governorate_id) REFERENCES saas.governorates (id) ON DELETE RESTRICT,
        CONSTRAINT fk_tenants_plans_plan_id FOREIGN KEY (plan_id) REFERENCES saas.plans (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.antibiotics (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(20) NOT NULL,
        name_en character varying(200) NOT NULL,
        name_ar character varying(200) NOT NULL,
        abbreviation character varying(10) NOT NULL,
        commercial_name_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        is_active boolean NOT NULL,
        CONSTRAINT pk_antibiotics PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_antibiotics_commercial_names FOREIGN KEY (tenant_id, commercial_name_id) REFERENCES app.commercial_names (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.contract_pricings (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        payer_id uuid NOT NULL,
        contract_tier_id uuid NOT NULL,
        effective_from date NOT NULL,
        effective_to date,
        applied_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_contract_pricings PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_contract_pricings_payer FOREIGN KEY (tenant_id, payer_id) REFERENCES app.payers (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_contract_pricings_tier FOREIGN KEY (tenant_id, contract_tier_id) REFERENCES app.contract_tiers (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.services (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(16) NOT NULL,
        type character varying(40) NOT NULL,
        short_name character varying(50) NOT NULL,
        shortcut_no integer,
        name_en character varying(400) NOT NULL,
        name_ar character varying(400) NOT NULL,
        report_name character varying(200),
        loinc character varying(20),
        cpt character varying(20),
        clinical_group_id uuid,
        sample_type_id uuid,
        sample_condition_id uuid,
        header character varying(200),
        aliases text[] NOT NULL,
        precaution character varying(1000),
        gender character varying(40) NOT NULL,
        turnaround_hours integer NOT NULL,
        label_count integer NOT NULL,
        print_alone boolean NOT NULL,
        is_orderable boolean NOT NULL,
        show_on_web boolean NOT NULL,
        profile_sum numeric(12,4),
        sequence integer NOT NULL,
        is_active boolean NOT NULL,
        revision integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_services PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_services_gender_domain CHECK ("gender" IN ('Any', 'Male', 'Female')),
        CONSTRAINT ck_services_type_domain CHECK ("type" IN ('Test', 'Profile', 'Package', 'Culture', 'TextReport', 'Additional')),
        CONSTRAINT fk_services_clinical_groups_tenant_id_clinical_group_id FOREIGN KEY (tenant_id, clinical_group_id) REFERENCES app.clinical_groups (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_services_sample_condition FOREIGN KEY (tenant_id, sample_condition_id) REFERENCES app.sample_conditions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_services_sample_types_tenant_id_sample_type_id FOREIGN KEY (tenant_id, sample_type_id) REFERENCES app.sample_types (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.tests (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        code character varying(16) NOT NULL,
        name_en character varying(400) NOT NULL,
        name_ar character varying(400) NOT NULL,
        report_name character varying(200),
        loinc character varying(20),
        cpt character varying(20),
        clinical_group_id uuid NOT NULL,
        type character varying(40) NOT NULL,
        sample_type_id uuid,
        sample_condition_id uuid,
        unit_id uuid,
        gender character varying(40) NOT NULL,
        methodology character varying(200),
        decimals integer NOT NULL,
        is_hidden_helper boolean NOT NULL,
        print_reference boolean NOT NULL,
        label_count integer NOT NULL,
        prefix character varying(10),
        duration_hours integer NOT NULL,
        min_duration_hours integer NOT NULL,
        frequency_check_days integer,
        frequency_action character varying(40) NOT NULL,
        delta_percent numeric(9,3),
        delta_window_days integer,
        test_days integer NOT NULL,
        formula_expression character varying(1000),
        formula_references text[] NOT NULL,
        is_active boolean NOT NULL,
        revision integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_tests PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_tests_frequency_action_domain CHECK ("frequency_action" IN ('Alert', 'Prevent')),
        CONSTRAINT ck_tests_gender_domain CHECK ("gender" IN ('Any', 'Male', 'Female')),
        CONSTRAINT ck_tests_type_domain CHECK ("type" IN ('Numeric', 'NonNumeric', 'OptionalList', 'RestrictedList', 'Formula', 'FormattedText', 'Culture')),
        CONSTRAINT fk_tests_clinical_groups_tenant_id_clinical_group_id FOREIGN KEY (tenant_id, clinical_group_id) REFERENCES app.clinical_groups (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_tests_measurement_units_tenant_id_unit_id FOREIGN KEY (tenant_id, unit_id) REFERENCES app.measurement_units (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_tests_sample_condition FOREIGN KEY (tenant_id, sample_condition_id) REFERENCES app.sample_conditions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_tests_sample_types_tenant_id_sample_type_id FOREIGN KEY (tenant_id, sample_type_id) REFERENCES app.sample_types (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.wallets (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        balance numeric(14,2) NOT NULL,
        currency_code character(3) NOT NULL,
        next_charge_at_utc timestamp with time zone NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_wallets PRIMARY KEY (id),
        CONSTRAINT fk_wallets_tenants_tenant_id FOREIGN KEY (tenant_id) REFERENCES saas.tenants (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.contract_prices (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        contract_pricing_id uuid NOT NULL,
        service_id uuid NOT NULL,
        list_price numeric(12,2) NOT NULL,
        patient_share numeric(12,2) NOT NULL,
        payer_share numeric(12,2) NOT NULL,
        is_excluded boolean NOT NULL,
        alert_condition character varying(500),
        source_list_id uuid,
        CONSTRAINT pk_contract_prices PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_contract_prices_pricing FOREIGN KEY (tenant_id, contract_pricing_id) REFERENCES app.contract_pricings (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.contract_pricing_sources (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        contract_pricing_id uuid NOT NULL,
        priority integer NOT NULL,
        base_price_list_id uuid NOT NULL,
        patient_factor numeric(9,4) NOT NULL,
        payer_factor numeric(9,4) NOT NULL,
        CONSTRAINT pk_contract_pricing_sources PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_contract_pricing_sources_priority CHECK (priority BETWEEN 1 AND 3),
        CONSTRAINT fk_contract_pricing_sources_list FOREIGN KEY (tenant_id, base_price_list_id) REFERENCES app.base_price_lists (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_contract_pricing_sources_pricing FOREIGN KEY (tenant_id, contract_pricing_id) REFERENCES app.contract_pricings (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.service_members (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        service_id uuid NOT NULL,
        kind character varying(40) NOT NULL,
        member_id uuid NOT NULL,
        "order" integer NOT NULL,
        in_sum boolean NOT NULL,
        visible boolean NOT NULL,
        CONSTRAINT pk_service_members PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_service_members_kind_domain CHECK ("kind" IN ('Test', 'Service')),
        CONSTRAINT fk_service_members_services_tenant_id_service_id FOREIGN KEY (tenant_id, service_id) REFERENCES app.services (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.test_list_values (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        lab_test_id uuid NOT NULL,
        value character varying(200) NOT NULL,
        is_normal boolean NOT NULL,
        is_critical boolean NOT NULL,
        is_default boolean NOT NULL,
        is_placeholder boolean NOT NULL,
        colour character varying(7),
        display_order integer NOT NULL,
        CONSTRAINT pk_test_list_values PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_test_list_values_tests_tenant_id_lab_test_id FOREIGN KEY (tenant_id, lab_test_id) REFERENCES app.tests (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.test_paragraphs (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        lab_test_id uuid NOT NULL,
        title character varying(200) NOT NULL,
        text character varying(4000),
        display_order integer NOT NULL,
        CONSTRAINT pk_test_paragraphs PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_test_paragraphs_tests_tenant_id_lab_test_id FOREIGN KEY (tenant_id, lab_test_id) REFERENCES app.tests (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE app.test_range_bands (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        lab_test_id uuid NOT NULL,
        sex character varying(40) NOT NULL,
        age_from integer NOT NULL,
        age_to integer NOT NULL,
        age_unit character varying(40) NOT NULL,
        patient_condition_id uuid,
        low numeric(18,6),
        high numeric(18,6),
        panic_low numeric(18,6),
        panic_high numeric(18,6),
        reportable_low numeric(18,6),
        reportable_high numeric(18,6),
        text_range character varying(200),
        display_order integer NOT NULL,
        CONSTRAINT pk_test_range_bands PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_test_range_bands_age_unit_domain CHECK ("age_unit" IN ('Days', 'Months', 'Years')),
        CONSTRAINT ck_test_range_bands_sex_domain CHECK ("sex" IN ('Any', 'Male', 'Female')),
        CONSTRAINT fk_test_range_bands_patient_condition FOREIGN KEY (tenant_id, patient_condition_id) REFERENCES app.patient_conditions (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_test_range_bands_tests_tenant_id_lab_test_id FOREIGN KEY (tenant_id, lab_test_id) REFERENCES app.tests (tenant_id, id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.wallet_top_up_requests (
        id uuid NOT NULL,
        wallet_id uuid NOT NULL,
        amount numeric(14,2) NOT NULL,
        note character varying(500),
        requested_by_user_id uuid NOT NULL,
        requested_at_utc timestamp with time zone NOT NULL,
        status character varying(40) NOT NULL,
        decided_at_utc timestamp with time zone,
        transaction_id uuid,
        CONSTRAINT pk_wallet_top_up_requests PRIMARY KEY (id),
        CONSTRAINT ck_wallet_top_up_requests_status_domain CHECK ("status" IN ('Pending', 'Completed', 'Declined')),
        CONSTRAINT fk_wallet_top_up_requests_wallets_wallet_id FOREIGN KEY (wallet_id) REFERENCES saas.wallets (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE TABLE saas.wallet_transactions (
        id uuid NOT NULL,
        wallet_id uuid NOT NULL,
        kind character varying(40) NOT NULL,
        amount numeric(14,2) NOT NULL,
        balance_after numeric(14,2) NOT NULL,
        reference character varying(100) NOT NULL,
        note character varying(500),
        by_user_id uuid,
        at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_wallet_transactions PRIMARY KEY (id),
        CONSTRAINT ck_wallet_transactions_kind_domain CHECK ("kind" IN ('TopUp', 'Subscription', 'Usage')),
        CONSTRAINT fk_wallet_transactions_wallets_wallet_id FOREIGN KEY (wallet_id) REFERENCES saas.wallets (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_tenant_memberships_primary_administrator ON app.tenant_memberships (tenant_id) WHERE is_primary_administrator;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_agent_log_entries_agent_time ON app.agent_log_entries (tenant_id, agent_id, at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_analyser_flags_analyser ON app.analyser_flags (tenant_id, analyser_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_analyser_models_vendor_model ON saas.analyser_models (vendor, model);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_analyser_result_intake_analyser ON app.analyser_result_intake (tenant_id, analyser_id, at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_analyser_result_intake_sample ON app.analyser_result_intake (tenant_id, sample_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_analyser_result_intake_status ON app.analyser_result_intake (tenant_id, status, at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_analyser_result_intake_time ON app.analyser_result_intake (tenant_id, at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_analyser_sample_codes_type ON app.analyser_sample_codes (tenant_id, analyser_id, sample_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_analyser_test_mappings_test ON app.analyser_test_mappings (tenant_id, test_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_analyser_test_mappings_code ON app.analyser_test_mappings (tenant_id, analyser_id, analyser_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_analyser_value_conversions_mapping ON app.analyser_value_conversions (tenant_id, mapping_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_analysers_clinical_group ON app.analysers (tenant_id, clinical_group_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_analysers_site_agent ON app.analysers (tenant_id, site_agent_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_analysers_code ON app.analysers (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_antibiotics_tenant_id_code ON app.antibiotics (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_antibiotics_tenant_id_commercial_name_id ON app.antibiotics (tenant_id, commercial_name_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_antibiotics_tenant_id_is_active ON app.antibiotics (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_base_price_lines_tenant_id_service_id ON app.base_price_lines (tenant_id, service_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_base_price_lines_service ON app.base_price_lines (tenant_id, price_list_id, service_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_base_price_lists_tenant_id_is_active_year ON app.base_price_lists (tenant_id, is_active, year);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_base_price_lists_code ON app.base_price_lists (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_branches_tenant_id_code ON app.branches (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_branches_tenant_id_is_active ON app.branches (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_cash_closings_cashier_user_id ON app.cash_closings (cashier_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_cash_closings_closed_by_user_id ON app.cash_closings (closed_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_cash_closings_tenant_id_closed_at_utc ON app.cash_closings (tenant_id, closed_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_cash_closings_tenant_id_number ON app.cash_closings (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_cash_closings_tenant_id_treasury_id_period_from_utc ON app.cash_closings (tenant_id, treasury_id, period_from_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_clinical_groups_tenant_id_code ON app.clinical_groups (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_clinical_groups_tenant_id_is_active_display_order ON app.clinical_groups (tenant_id, is_active, display_order);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_code_systems_name_version ON saas.code_systems (name, version);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_comment_templates_tenant_id_clinical_group_id ON app.comment_templates (tenant_id, clinical_group_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_comment_templates_tenant_id_is_active ON app.comment_templates (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_comment_templates_tenant_id_short_name ON app.comment_templates (tenant_id, short_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_commercial_names_tenant_id_drug_type_id ON app.commercial_names (tenant_id, drug_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_commercial_names_tenant_id_is_active ON app.commercial_names (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_contract_prices_service ON app.contract_prices (tenant_id, contract_pricing_id, service_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_contract_pricing_sources_list ON app.contract_pricing_sources (tenant_id, base_price_list_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_contract_pricing_sources_pricing ON app.contract_pricing_sources (tenant_id, contract_pricing_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_contract_pricings_tenant_id_payer_id ON app.contract_pricings (tenant_id, payer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_contract_pricings_tier ON app.contract_pricings (tenant_id, contract_tier_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_contract_tiers_tenant_id_payer_id ON app.contract_tiers (tenant_id, payer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_countries_iso_code ON saas.countries (iso_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_diagnoses_tenant_id_icd_code ON app.diagnoses (tenant_id, icd_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_diagnoses_tenant_id_is_active ON app.diagnoses (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_doctors_tenant_id_code ON app.doctors (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_doctors_tenant_id_is_active ON app.doctors (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_drug_types_tenant_id_is_active ON app.drug_types (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_governorates_country_id ON saas.governorates (country_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_machine_credentials_tenant ON identity.machine_credentials (tenant_id, revoked_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_machine_credentials_client_id ON identity.machine_credentials (client_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_machine_credentials_service_user ON identity.machine_credentials (service_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_measurement_units_tenant_id_name_key ON app.measurement_units (tenant_id, name_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_notification_templates_code ON saas.notification_templates (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_organisms_tenant_id_code ON app.organisms (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_organisms_tenant_id_is_active ON app.organisms (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_patient_conditions_tenant_id_name_key ON app.patient_conditions (tenant_id, name_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_patient_documents_tenant_id_patient_id ON app.patient_documents (tenant_id, patient_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_patient_documents_tenant_id_storage_key ON app.patient_documents (tenant_id, storage_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_patients_tenant_id_merged_into_patient_id ON app.patients (tenant_id, merged_into_patient_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_patients_tenant_id_mobile ON app.patients (tenant_id, mobile);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_patients_tenant_id_name_key ON app.patients (tenant_id, name_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_patients_tenant_id_national_id ON app.patients (tenant_id, national_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_patients_tenant_id_number ON app.patients (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_patients_tenant_id_status ON app.patients (tenant_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_payers_tenant_id_is_active ON app.payers (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_payers_default ON app.payers (tenant_id) WHERE is_default;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_payers_search_key ON app.payers (tenant_id, search_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_payers_web_username ON app.payers (tenant_id, web_username) WHERE web_username IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_payments_cashier_user_id ON app.payments (cashier_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_payments_tenant_id_batch_id ON app.payments (tenant_id, batch_id) WHERE batch_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_payments_tenant_id_cashier_user_id_paid_at_utc ON app.payments (tenant_id, cashier_user_id, paid_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_payments_tenant_id_paid_at_utc ON app.payments (tenant_id, paid_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_payments_tenant_id_receipt_number ON app.payments (tenant_id, receipt_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_payments_tenant_id_treasury_id_paid_at_utc ON app.payments (tenant_id, treasury_id, paid_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_payments_tenant_id_visit_id ON app.payments (tenant_id, visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_payments_voided_by_user_id ON app.payments (voided_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_pcr_kit_genes_kit ON app.pcr_kit_genes (tenant_id, kit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_pcr_kit_templates_analyser_model_id ON saas.pcr_kit_templates (analyser_model_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_pcr_kit_templates_name ON saas.pcr_kit_templates (name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_pcr_kits_tenant_id_analyser_id ON app.pcr_kits (tenant_id, analyser_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_pcr_kits_name ON app.pcr_kits (tenant_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_plans_code ON saas.plans (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_preparations_tenant_id_code ON app.preparations (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_preparations_tenant_id_is_active ON app.preparations (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_reasons_tenant_id_is_active ON app.reasons (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_reasons_tenant_id_kind ON app.reasons (tenant_id, kind);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_refunds_cashier_user_id ON app.refunds (cashier_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_refunds_tenant_id_number ON app.refunds (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_refunds_tenant_id_refunded_at_utc ON app.refunds (tenant_id, refunded_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_refunds_tenant_id_treasury_id_refunded_at_utc ON app.refunds (tenant_id, treasury_id, refunded_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_refunds_tenant_id_visit_id ON app.refunds (tenant_id, visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_report_templates_tenant_id_is_active ON app.report_templates (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_report_templates_tenant_id_type ON app.report_templates (tenant_id, type);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_role_result_rights_group ON app.role_result_rights (tenant_id, clinical_group_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_role_result_rights_scope ON app.role_result_rights (tenant_id, role_id, clinical_group_id, branch_id) NULLS NOT DISTINCT;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_rule_templates_name ON saas.rule_templates (name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_sample_conditions_tenant_id_sample_type_id_code ON app.sample_conditions (tenant_id, sample_type_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_sample_events_tenant_id_sample_id_at_utc ON app.sample_events (tenant_id, sample_id, at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_sample_types_tenant_id_code ON app.sample_types (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_samples_tenant_id_accession ON app.samples (tenant_id, accession);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_samples_tenant_id_ordered_at_utc ON app.samples (tenant_id, ordered_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_samples_tenant_id_sample_number ON app.samples (tenant_id, sample_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_samples_tenant_id_status_status_changed_at_utc ON app.samples (tenant_id, status, status_changed_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_samples_tenant_id_tube_id_generation ON app.samples (tenant_id, tube_id, generation);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_samples_tenant_id_visit_id ON app.samples (tenant_id, visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_sending_labs_tenant_id_doctor_id ON app.sending_labs (tenant_id, doctor_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_sending_labs_tenant_id_is_active ON app.sending_labs (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_sending_labs_tenant_id_payer_id ON app.sending_labs (tenant_id, payer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_sensitivity_values_tenant_id_code ON app.sensitivity_values (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_sensitivity_values_tenant_id_is_active ON app.sensitivity_values (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_service_members_tenant_id_member_id ON app.service_members (tenant_id, member_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_service_members_member ON app.service_members (tenant_id, service_id, kind, member_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_service_members_order ON app.service_members (tenant_id, service_id, "order");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_services_tenant_id_clinical_group_id ON app.services (tenant_id, clinical_group_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_services_tenant_id_code ON app.services (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_services_tenant_id_sample_condition_id ON app.services (tenant_id, sample_condition_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_services_tenant_id_sample_type_id ON app.services (tenant_id, sample_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_services_tenant_id_type_is_active ON app.services (tenant_id, type, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_services_shortcut_no ON app.services (tenant_id, shortcut_no) WHERE shortcut_no IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_shortcut_sets_tenant_id_code ON app.shortcut_sets (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_shortcut_sets_tenant_id_is_active ON app.shortcut_sets (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_site_agents_branch ON app.site_agents (tenant_id, branch_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_site_agents_machine_credential_id ON app.site_agents (machine_credential_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_site_agents_service_user ON app.site_agents (service_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ux_site_agents_name ON app.site_agents (tenant_id, name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_subscription_requests_expires_at_utc ON saas.subscription_requests (expires_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_support_ticket_replies_ticket_at ON saas.support_ticket_replies (support_ticket_id, at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_support_tickets_opened_by_user_id ON saas.support_tickets (opened_by_user_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_support_tickets_status_activity ON saas.support_tickets (status, last_activity_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_support_tickets_tenant_activity ON saas.support_tickets (tenant_id, last_activity_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_support_tickets_tenant_id_number ON saas.support_tickets (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_tenants_country_id ON saas.tenants (country_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_tenants_governorate_id ON saas.tenants (governorate_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_tenants_plan_id ON saas.tenants (plan_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_tenants_portal_slug ON saas.tenants (portal_slug);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_tenants_status ON saas.tenants (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_test_list_values_tenant_id_lab_test_id ON app.test_list_values (tenant_id, lab_test_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_test_paragraphs_tenant_id_lab_test_id ON app.test_paragraphs (tenant_id, lab_test_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_test_range_bands_patient_condition ON app.test_range_bands (tenant_id, patient_condition_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_test_range_bands_tenant_id_lab_test_id ON app.test_range_bands (tenant_id, lab_test_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_test_rules_tenant_id_clinical_group_id ON app.test_rules (tenant_id, clinical_group_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_test_rules_tenant_id_is_active ON app.test_rules (tenant_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_tests_tenant_id_clinical_group_id_is_active ON app.tests (tenant_id, clinical_group_id, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_tests_tenant_id_code ON app.tests (tenant_id, code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_tests_tenant_id_sample_condition_id ON app.tests (tenant_id, sample_condition_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_tests_tenant_id_sample_type_id ON app.tests (tenant_id, sample_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_tests_tenant_id_unit_id ON app.tests (tenant_id, unit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_wallet_top_up_requests_wallet_id_status ON saas.wallet_top_up_requests (wallet_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE INDEX ix_wallet_transactions_wallet_id_at_utc ON saas.wallet_transactions (wallet_id, at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    CREATE UNIQUE INDEX ix_wallets_tenant_id ON saas.wallets (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    ALTER TABLE app.agent_log_entries ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.agent_log_entries FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.agent_log_entries
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.analyser_connection_states ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.analyser_connection_states FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.analyser_connection_states
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.analyser_result_intake ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.analyser_result_intake FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.analyser_result_intake
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.analysers ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.analysers FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.analysers
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.base_price_lists ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.base_price_lists FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.base_price_lists
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.branches ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.branches FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.branches
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.cash_closings ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.cash_closings FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.cash_closings
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.clinical_groups ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.clinical_groups FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.clinical_groups
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.comment_templates ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.comment_templates FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.comment_templates
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.diagnoses ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.diagnoses FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.diagnoses
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.doctors ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.doctors FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.doctors
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.drug_types ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.drug_types FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.drug_types
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.measurement_units ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.measurement_units FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.measurement_units
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.organisms ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.organisms FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.organisms
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.patient_conditions ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.patient_conditions FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.patient_conditions
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.patients ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.patients FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.patients
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.payers ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.payers FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.payers
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.preparations ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.preparations FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.preparations
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.reasons ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.reasons FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.reasons
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.report_templates ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.report_templates FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.report_templates
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.role_result_rights ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.role_result_rights FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.role_result_rights
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.sample_types ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.sample_types FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.sample_types
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.samples ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.samples FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.samples
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.sensitivity_values ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.sensitivity_values FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.sensitivity_values
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.shortcut_sets ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.shortcut_sets FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.shortcut_sets
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.site_agent_states ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.site_agent_states FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.site_agent_states
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.test_rules ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.test_rules FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.test_rules
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.visit_accounts ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.visit_accounts FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.visit_accounts
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.analyser_flags ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.analyser_flags FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.analyser_flags
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.analyser_sample_codes ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.analyser_sample_codes FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.analyser_sample_codes
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.analyser_test_mappings ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.analyser_test_mappings FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.analyser_test_mappings
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.pcr_kits ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.pcr_kits FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.pcr_kits
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.base_price_lines ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.base_price_lines FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.base_price_lines
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.sending_labs ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.sending_labs FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.sending_labs
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.commercial_names ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.commercial_names FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.commercial_names
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.site_agents ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.site_agents FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.site_agents
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.patient_documents ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.patient_documents FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.patient_documents
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.contract_tiers ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.contract_tiers FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.contract_tiers
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.sample_conditions ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.sample_conditions FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.sample_conditions
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.sample_events ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.sample_events FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.sample_events
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.payments ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.payments FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.payments
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.refunds ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.refunds FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.refunds
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.analyser_value_conversions ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.analyser_value_conversions FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.analyser_value_conversions
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.pcr_kit_genes ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.pcr_kit_genes FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.pcr_kit_genes
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.antibiotics ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.antibiotics FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.antibiotics
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.contract_pricings ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.contract_pricings FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.contract_pricings
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.services ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.services FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.services
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.tests ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.tests FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.tests
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.contract_prices ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.contract_prices FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.contract_prices
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.contract_pricing_sources ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.contract_pricing_sources FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.contract_pricing_sources
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.service_members ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.service_members FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.service_members
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.test_list_values ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.test_list_values FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.test_list_values
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.test_paragraphs ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.test_paragraphs FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.test_paragraphs
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.test_range_bands ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.test_range_bands FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.test_range_bands
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    -- Account & onboarding (intake LIS-INT-ACC-001): grants and the row-level security of the support desk tables.
    -- Appended by the integrator to the wave migration after saas.support_tickets and saas.support_ticket_replies exist;
    -- plain SQL, safe to run twice (DROP POLICY IF EXISTS before CREATE POLICY, idempotent GRANT and ALTER TABLE).

    -- 1. Grants: lis_app has no default privileges in saas (deploy/postgres/schema.sql). No DELETE: tickets and replies
    --    are kept for the record (BR-15); a ticket is closed, never removed.
    GRANT USAGE ON SCHEMA saas TO lis_app;
    GRANT SELECT, INSERT, UPDATE ON saas.support_tickets, saas.support_ticket_replies TO lis_app;

    -- 2. INV-SUP-004 in the database (defence in depth behind SupportTicketRepository.Scoped): a laboratory reaches only
    --    its own tickets; the platform tenant (the support desk) reaches every laboratory's, without elevation. This is a
    --    platform-level table, not an app tenant table: no tenant table's policy is widened (MODULES.md, acc row).
    ALTER TABLE saas.support_tickets ENABLE ROW LEVEL SECURITY;
    ALTER TABLE saas.support_tickets FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS support_desk_scope ON saas.support_tickets;
    CREATE POLICY support_desk_scope ON saas.support_tickets
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.current_tenant', true) = '00000000-0000-0000-0000-000000000001'
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.current_tenant', true) = '00000000-0000-0000-0000-000000000001'
            OR current_setting('app.bypass_rls', true) = 'on'
        );

    -- A reply is visible and writable exactly when its ticket is (the sub-select is itself bounded by the policy above).
    ALTER TABLE saas.support_ticket_replies ENABLE ROW LEVEL SECURITY;
    ALTER TABLE saas.support_ticket_replies FORCE ROW LEVEL SECURITY;
    DROP POLICY IF EXISTS support_desk_scope ON saas.support_ticket_replies;
    CREATE POLICY support_desk_scope ON saas.support_ticket_replies
        USING (EXISTS (SELECT 1 FROM saas.support_tickets t WHERE t.id = support_ticket_replies.support_ticket_id))
        WITH CHECK (EXISTS (SELECT 1 FROM saas.support_tickets t WHERE t.id = support_ticket_replies.support_ticket_id));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    -- Integration module (intake LIS-INT-INT-001): extra SQL appended by the integrator to the wave-3 migration. Idempotent.
    -- The tables, keys, indexes and CHECK constraints come from the EF model; FORCE RLS + tenant_isolation on every app.*
    -- table of the module (analysers, analyser_flags, analyser_test_mappings, analyser_value_conversions,
    -- analyser_sample_codes, site_agents, pcr_kits, pcr_kit_genes, site_agent_states, analyser_connection_states,
    -- agent_log_entries, analyser_result_intake) come from scripts/add-rls-to-migration.mjs.
    -- identity.machine_credentials has no RLS (like identity.refresh_sessions: read by client id before a tenant is known);
    -- lis_app already holds SELECT, INSERT, UPDATE on the identity schema through its default privileges (schema.sql).

    -- 1. The platform library of analyser models (SA-15) is read by tenants (CP-58 model picker): SELECT only, when present.
    DO $$
    BEGIN
        IF to_regclass('saas.analyser_models') IS NOT NULL THEN
            GRANT USAGE ON SCHEMA saas TO lis_app;
            GRANT SELECT ON saas.analyser_models TO lis_app;
        END IF;
    END
    $$;

    -- 2. Append-only logs: the agent log and the result intake are evidence (WF-07 "everything pending is traced"); nobody,
    --    the owner included, updates or deletes a row. Retention purges, when built, run as a dedicated maintenance role.
    CREATE OR REPLACE FUNCTION app.integration_refuse_change() RETURNS trigger
        LANGUAGE plpgsql AS
    $$
    BEGIN
        RAISE EXCEPTION 'append-only table %.%: % refused', TG_TABLE_SCHEMA, TG_TABLE_NAME, TG_OP USING ERRCODE = '42501';
    END
    $$;

    DO $$
    BEGIN
        IF to_regclass('app.agent_log_entries') IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM pg_trigger WHERE tgname = 'trg_agent_log_entries_append_only' AND tgrelid = 'app.agent_log_entries'::regclass) THEN
            CREATE TRIGGER trg_agent_log_entries_append_only BEFORE UPDATE OR DELETE ON app.agent_log_entries
                FOR EACH ROW EXECUTE FUNCTION app.integration_refuse_change();
        END IF;

        IF to_regclass('app.analyser_result_intake') IS NOT NULL AND NOT EXISTS (
            SELECT 1 FROM pg_trigger WHERE tgname = 'trg_analyser_result_intake_append_only' AND tgrelid = 'app.analyser_result_intake'::regclass) THEN
            CREATE TRIGGER trg_analyser_result_intake_append_only BEFORE UPDATE OR DELETE ON app.analyser_result_intake
                FOR EACH ROW EXECUTE FUNCTION app.integration_refuse_change();
        END IF;
    END
    $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    -- Platform administration (intake LIS-INT-PLT-001): grants, the indexes EF cannot express, the cross-tenant count
    -- function and the idempotent seed. Appended by the integrator to the wave migration after the saas tables exist;
    -- plain SQL, safe to run twice (IF NOT EXISTS, CREATE OR REPLACE, ON CONFLICT DO NOTHING on natural keys).

    -- 1. Grants: lis_app has no default privileges in saas (deploy/postgres/schema.sql).
    GRANT USAGE ON SCHEMA saas TO lis_app;
    GRANT SELECT, INSERT, UPDATE ON
        saas.tenants, saas.plans, saas.wallets, saas.wallet_transactions, saas.wallet_top_up_requests,
        saas.notification_templates, saas.analyser_models, saas.pcr_kit_templates, saas.code_systems, saas.code_system_items,
        saas.rule_templates, saas.subscription_requests
        TO lis_app;
    GRANT SELECT, INSERT, UPDATE, DELETE ON saas.countries, saas.governorates TO lis_app;   -- SA-10: an unreferenced country may be deleted

    -- 2. INV-PLT-011: a governorate's English name is unique within its country (case-insensitive).
    CREATE UNIQUE INDEX IF NOT EXISTS ux_governorates_country_name ON saas.governorates (country_id, lower(name_en));

    -- 3. SA-04 branch and user counts across tenants without elevation: SECURITY DEFINER (owner lis_owner, BYPASSRLS),
    --    counts only, and only for a session whose tenant is the platform tenant.
    CREATE OR REPLACE FUNCTION saas.client_usage_counts(p_tenant_ids uuid[])
    RETURNS TABLE (tenant_id uuid, branches bigint, users bigint)
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
    AS $fn$
    #variable_conflict use_column
    BEGIN
        IF coalesce(current_setting('app.current_tenant', true), '') <> '00000000-0000-0000-0000-000000000001' THEN
            RAISE EXCEPTION 'saas.client_usage_counts is reserved to the platform tenant' USING ERRCODE = '42501';
        END IF;

        IF to_regclass('app.branches') IS NULL THEN
            RETURN QUERY
                SELECT t.id,
                       0::bigint,
                       (SELECT count(*) FROM app.tenant_memberships m WHERE m.tenant_id = t.id AND m.is_active)
                FROM unnest(p_tenant_ids) AS t(id);
        ELSE
            RETURN QUERY EXECUTE
                'SELECT t.id,
                        (SELECT count(*) FROM app.branches b WHERE b.tenant_id = t.id),
                        (SELECT count(*) FROM app.tenant_memberships m WHERE m.tenant_id = t.id AND m.is_active)
                 FROM unnest($1) AS t(id)'
                USING p_tenant_ids;
        END IF;
    END
    $fn$;
    REVOKE ALL ON FUNCTION saas.client_usage_counts(uuid[]) FROM PUBLIC;
    GRANT EXECUTE ON FUNCTION saas.client_usage_counts(uuid[]) TO lis_app;

    -- 4. Seed: countries (SA-10), the governorates of Egypt (SA-11), plans (SA-05), notification templates (SA-20).
    INSERT INTO saas.countries (id, name_en, name_ar, iso_code, dial_code, currency, currency_code, language, time_zone, mobile_length, flag, remarks, is_active, created_at_utc, created_by_display_name)
    VALUES
        (md5('lis:country:EG')::uuid, 'Egypt', 'مصر', 'EG', '+20', 'Egyptian Pound', 'EGP', 'ar', 'Africa/Cairo', 10, '🇪🇬', NULL, true, now(), 'seed'),
        (md5('lis:country:SA')::uuid, 'Saudi Arabia', 'المملكة العربية السعودية', 'SA', '+966', 'Saudi Riyal', 'SAR', 'ar', 'Asia/Riyadh', 9, '🇸🇦', NULL, true, now(), 'seed'),
        (md5('lis:country:AE')::uuid, 'United Arab Emirates', 'الإمارات العربية المتحدة', 'AE', '+971', 'UAE Dirham', 'AED', 'ar', 'Asia/Dubai', 9, '🇦🇪', NULL, true, now(), 'seed'),
        (md5('lis:country:KW')::uuid, 'Kuwait', 'الكويت', 'KW', '+965', 'Kuwaiti Dinar', 'KWD', 'ar', 'Asia/Kuwait', 8, '🇰🇼', NULL, true, now(), 'seed'),
        (md5('lis:country:JO')::uuid, 'Jordan', 'الأردن', 'JO', '+962', 'Jordanian Dinar', 'JOD', 'ar', 'Asia/Amman', 9, '🇯🇴', NULL, true, now(), 'seed')
    ON CONFLICT (iso_code) DO NOTHING;

    INSERT INTO saas.governorates (id, country_id, name_en, name_ar, district, remarks, is_active, created_at_utc, created_by_display_name)
    SELECT md5('lis:governorate:EG:' || g.name_en)::uuid, c.id, g.name_en, g.name_ar, NULL, NULL, true, now(), 'seed'
    FROM saas.countries c
    CROSS JOIN (VALUES
        ('Cairo', 'القاهرة'), ('Giza', 'الجيزة'), ('Alexandria', 'الإسكندرية'), ('Qalyubia', 'القليوبية'), ('Sharqia', 'الشرقية'),
        ('Dakahlia', 'الدقهلية'), ('Gharbia', 'الغربية'), ('Monufia', 'المنوفية'), ('Beheira', 'البحيرة'), ('Kafr El Sheikh', 'كفر الشيخ'),
        ('Damietta', 'دمياط'), ('Port Said', 'بورسعيد'), ('Ismailia', 'الإسماعيلية'), ('Suez', 'السويس'), ('North Sinai', 'شمال سيناء'),
        ('South Sinai', 'جنوب سيناء'), ('Faiyum', 'الفيوم'), ('Beni Suef', 'بني سويف'), ('Minya', 'المنيا'), ('Asyut', 'أسيوط'),
        ('Sohag', 'سوهاج'), ('Qena', 'قنا'), ('Luxor', 'الأقصر'), ('Aswan', 'أسوان'), ('Red Sea', 'البحر الأحمر'),
        ('New Valley', 'الوادي الجديد'), ('Matrouh', 'مطروح')
    ) AS g(name_en, name_ar)
    WHERE c.iso_code = 'EG'
    ON CONFLICT (country_id, lower(name_en)) DO NOTHING;

    INSERT INTO saas.plans (id, code, name_en, name_ar, monthly_fee, currency_code, max_branches, max_users, sms_quota, max_analysers, is_active, created_at_utc, created_by_display_name)
    VALUES
        (md5('lis:plan:STARTER')::uuid, 'STARTER', 'Starter', 'الباقة الأساسية', 1500.00, 'EGP', 1, 5, 500, 1, true, now(), 'seed'),
        (md5('lis:plan:STANDARD')::uuid, 'STANDARD', 'Standard', 'الباقة القياسية', 3500.00, 'EGP', 3, 20, 2000, 5, true, now(), 'seed'),
        (md5('lis:plan:ENTERPRISE')::uuid, 'ENTERPRISE', 'Enterprise', 'باقة المؤسسات', 9000.00, 'EGP', 20, 200, 10000, 50, true, now(), 'seed')
    ON CONFLICT (code) DO NOTHING;

    INSERT INTO saas.notification_templates (id, code, channel, subject_en, subject_ar, text_en, text_ar, is_active, created_at_utc, created_by_display_name)
    VALUES
        (md5('lis:template:otp')::uuid, 'otp', 'Sms', NULL, NULL,
         'Your Sky LIS verification code is {code}. It is valid for {minutes} minutes. Never share it.',
         'رمز التحقق الخاص بك في Sky LIS هو {code}. صالح لمدة {minutes} دقائق. لا تشاركه مع أحد.', true, now(), 'seed'),
        (md5('lis:template:password-recovery')::uuid, 'password-recovery', 'Sms', NULL, NULL,
         'Use {code} to reset your Sky LIS password. The code expires in {minutes} minutes. Ignore this message if you did not ask for it.',
         'استخدم الرمز {code} لإعادة تعيين كلمة مرور Sky LIS. ينتهي الرمز خلال {minutes} دقائق. تجاهل هذه الرسالة إن لم تطلبها.', true, now(), 'seed'),
        (md5('lis:template:portal-url')::uuid, 'portal-url', 'Sms', NULL, NULL,
         '{lab}: your Sky LIS portal is ready at {url}. Sign in with the username {username}.',
         '{lab}: بوابة Sky LIS الخاصة بكم جاهزة على {url}. سجّلوا الدخول باسم المستخدم {username}.', true, now(), 'seed'),
        (md5('lis:template:treasury-request')::uuid, 'treasury-request', 'Sms', NULL, NULL,
         'Treasury access request {number}: {requester} asks for access to treasury {treasury}.',
         'طلب صلاحية الخزينة {number}: يطلب {requester} صلاحية على الخزينة {treasury}.', true, now(), 'seed'),
        (md5('lis:template:panic-value')::uuid, 'panic-value', 'Sms', NULL, NULL,
         'Panic value for {patient} (accession {acc}): {test} = {value}. Please act now.',
         'قيمة حرجة للمريض {patient} (رقم الطلب {acc}): {test} = {value}. يرجى التصرف فورًا.', true, now(), 'seed')
    ON CONFLICT (code) DO NOTHING;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010052829_Modules') THEN
    INSERT INTO ops.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261010052829_Modules', '9.0.20');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010053852_ModuleFixes1') THEN
    DROP INDEX app.ix_sample_events_tenant_id_sample_id_at_utc;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010053852_ModuleFixes1') THEN
    ALTER TABLE app.sample_events ADD sequence integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010053852_ModuleFixes1') THEN
    UPDATE app.sample_events AS e
    SET sequence = numbered.step
    FROM (
        SELECT tenant_id, id, row_number() OVER (PARTITION BY tenant_id, sample_id ORDER BY at_utc, id) AS step
        FROM app.sample_events
    ) AS numbered
    WHERE e.tenant_id = numbered.tenant_id AND e.id = numbered.id;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010053852_ModuleFixes1') THEN
    CREATE UNIQUE INDEX ix_sample_events_tenant_id_sample_id_sequence ON app.sample_events (tenant_id, sample_id, sequence);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010053852_ModuleFixes1') THEN
    INSERT INTO ops.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261010053852_ModuleFixes1', '9.0.20');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE TABLE app.batches (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        number character varying(20) NOT NULL,
        kind character varying(40) NOT NULL,
        branch_id uuid NOT NULL,
        payer_id uuid NOT NULL,
        contract_tier_id uuid NOT NULL,
        sending_lab_id uuid,
        referring_doctor_id uuid,
        group_name character varying(200),
        courier_en character varying(200),
        courier_ar character varying(200),
        notes character varying(1000),
        registered_at_utc timestamp with time zone NOT NULL,
        registered_by_user_id uuid NOT NULL,
        member_count integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_batches PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_batches_kind CHECK ((kind = 'LabToLab') = (sending_lab_id IS NOT NULL)),
        CONSTRAINT ck_batches_kind_domain CHECK ("kind" IN ('Group', 'LabToLab'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE TABLE app.bookings (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        number character varying(20) NOT NULL,
        branch_id uuid NOT NULL,
        patient_id uuid,
        patient_name character varying(200),
        gender character varying(10),
        mobile character varying(20),
        age_years integer,
        address character varying(500) NOT NULL,
        visit_at_utc timestamp with time zone NOT NULL,
        phlebotomist_user_id uuid,
        payer_id uuid NOT NULL,
        contract_tier_id uuid NOT NULL,
        service_ids uuid[] NOT NULL,
        notes character varying(1000),
        status character varying(40) NOT NULL,
        visit_id uuid,
        accession character varying(20),
        converted_at_utc timestamp with time zone,
        converted_by_user_id uuid,
        cancellation_reason character varying(1000),
        cancelled_by_user_id uuid,
        cancelled_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_bookings PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_bookings_cancellation CHECK ((status = 'Cancelled') = (cancellation_reason IS NOT NULL AND cancelled_at_utc IS NOT NULL)),
        CONSTRAINT ck_bookings_conversion CHECK ((status = 'Converted') = (visit_id IS NOT NULL AND accession IS NOT NULL AND converted_at_utc IS NOT NULL)),
        CONSTRAINT ck_bookings_status_domain CHECK ("status" IN ('Booked', 'Converted', 'Cancelled'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE TABLE app.visits (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        accession character varying(20) NOT NULL,
        patient_id uuid NOT NULL,
        original_patient_id uuid,
        branch_id uuid NOT NULL,
        location_type character varying(40) NOT NULL,
        treatment_no character varying(50),
        referring_doctor_id uuid,
        payer_id uuid NOT NULL,
        contract_tier_id uuid NOT NULL,
        insurance_id character varying(50),
        diagnosis_id uuid,
        booking_id uuid,
        batch_id uuid,
        urgent boolean NOT NULL,
        biohazard boolean NOT NULL,
        collected_outside boolean NOT NULL,
        utc_offset_minutes integer NOT NULL,
        status character varying(40) NOT NULL,
        registered_at_utc timestamp with time zone NOT NULL,
        registered_by_user_id uuid NOT NULL,
        completed_at_utc timestamp with time zone,
        total_list_price numeric(12,2) NOT NULL,
        discount_total numeric(12,2) NOT NULL,
        patient_share_total numeric(12,2) NOT NULL,
        payer_share_total numeric(12,2) NOT NULL,
        discount_kind character varying(40),
        discount_value numeric(12,2) NOT NULL,
        discount_reason_id uuid,
        discount_reason character varying(1000),
        discount_by_user_id uuid,
        discount_at_utc timestamp with time zone,
        revision integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_visits PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_visits_completion CHECK ((status = 'Completed') = (completed_at_utc IS NOT NULL)),
        CONSTRAINT ck_visits_discount_kind_domain CHECK ("discount_kind" IN ('Amount', 'Percent')),
        CONSTRAINT ck_visits_location_type_domain CHECK ("location_type" IN ('Walkin', 'Home', 'Batch', 'LabToLab')),
        CONSTRAINT ck_visits_status_domain CHECK ("status" IN ('Open', 'Completed')),
        CONSTRAINT ck_visits_totals CHECK (total_list_price >= 0 AND discount_total >= 0 AND patient_share_total >= 0 AND payer_share_total >= 0),
        CONSTRAINT fk_visits_batch FOREIGN KEY (tenant_id, batch_id) REFERENCES app.batches (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_visits_booking FOREIGN KEY (tenant_id, booking_id) REFERENCES app.bookings (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE TABLE app.visit_preparation_answers (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        visit_id uuid NOT NULL,
        preparation_id uuid NOT NULL,
        question_en character varying(400) NOT NULL,
        question_ar character varying(400) NOT NULL,
        answer character varying(400) NOT NULL,
        answered_at_utc timestamp with time zone NOT NULL,
        answered_by_user_id uuid NOT NULL,
        CONSTRAINT pk_visit_preparation_answers PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_visit_preparation_answers_visit FOREIGN KEY (tenant_id, visit_id) REFERENCES app.visits (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE TABLE app.visit_transfers (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        visit_id uuid NOT NULL,
        kind character varying(40) NOT NULL,
        line_id uuid,
        from_payer_id uuid,
        from_contract_tier_id uuid,
        to_payer_id uuid,
        to_contract_tier_id uuid,
        patient_share_before numeric(12,2) NOT NULL,
        patient_share_after numeric(12,2) NOT NULL,
        payer_share_before numeric(12,2) NOT NULL,
        payer_share_after numeric(12,2) NOT NULL,
        reason_id uuid,
        reason character varying(1000) NOT NULL,
        actor_user_id uuid NOT NULL,
        at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_visit_transfers PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_visit_transfers_kind_domain CHECK ("kind" IN ('Contract', 'LineShares')),
        CONSTRAINT fk_visit_transfers_visit FOREIGN KEY (tenant_id, visit_id) REFERENCES app.visits (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE TABLE app.visit_tubes (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        visit_id uuid NOT NULL,
        sample_number character varying(20) NOT NULL,
        clinical_group_id uuid NOT NULL,
        sample_type_id uuid NOT NULL,
        sample_condition_id uuid,
        unit_code character varying(8) NOT NULL,
        clinical_group_name_en character varying(400) NOT NULL,
        clinical_group_name_ar character varying(400) NOT NULL,
        sample_type_name_en character varying(400) NOT NULL,
        sample_type_name_ar character varying(400) NOT NULL,
        sample_condition_name_en character varying(400),
        sample_condition_name_ar character varying(400),
        created_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_visit_tubes PRIMARY KEY (tenant_id, id),
        CONSTRAINT fk_visit_tubes_visit FOREIGN KEY (tenant_id, visit_id) REFERENCES app.visits (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE TABLE app.visit_lines (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        visit_id uuid NOT NULL,
        service_id uuid NOT NULL,
        service_code character varying(32) NOT NULL,
        service_name_en character varying(400) NOT NULL,
        service_name_ar character varying(400) NOT NULL,
        service_type character varying(40) NOT NULL,
        kind character varying(40) NOT NULL,
        parent_line_id uuid,
        tube_id uuid,
        status character varying(40) NOT NULL,
        list_price numeric(12,2) NOT NULL,
        patient_share numeric(12,2) NOT NULL,
        payer_share numeric(12,2) NOT NULL,
        discount numeric(12,2) NOT NULL,
        expected_result_at_utc timestamp with time zone,
        test_ids uuid[] NOT NULL,
        collector_user_id uuid,
        origin character varying(40) NOT NULL,
        added_by_rule_id uuid,
        sequence integer NOT NULL,
        ordered_at_utc timestamp with time zone NOT NULL,
        ordered_by_user_id uuid NOT NULL,
        released_at_utc timestamp with time zone,
        released_by_user_id uuid,
        cancellation_reason_id uuid,
        cancellation_reason character varying(1000),
        cancelled_by_user_id uuid,
        cancelled_at_utc timestamp with time zone,
        CONSTRAINT pk_visit_lines PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_visit_lines_amounts CHECK (list_price >= 0 AND patient_share >= 0 AND payer_share >= 0 AND discount >= 0),
        CONSTRAINT ck_visit_lines_cancellation CHECK ((status = 'Cancelled') = (cancelled_at_utc IS NOT NULL AND cancellation_reason IS NOT NULL AND cancelled_by_user_id IS NOT NULL)),
        CONSTRAINT ck_visit_lines_kind_domain CHECK ("kind" IN ('Service', 'Package', 'PackageMember')),
        CONSTRAINT ck_visit_lines_member CHECK ((kind = 'PackageMember') = (parent_line_id IS NOT NULL)),
        CONSTRAINT ck_visit_lines_origin_domain CHECK ("origin" IN ('Manual', 'Rule', 'Diagnosis')),
        CONSTRAINT ck_visit_lines_status_domain CHECK ("status" IN ('Ordered', 'Reserved', 'Cancelled')),
        CONSTRAINT fk_visit_lines_parent FOREIGN KEY (tenant_id, parent_line_id) REFERENCES app.visit_lines (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_visit_lines_tube FOREIGN KEY (tenant_id, tube_id) REFERENCES app.visit_tubes (tenant_id, id) ON DELETE RESTRICT,
        CONSTRAINT fk_visit_lines_visit FOREIGN KEY (tenant_id, visit_id) REFERENCES app.visits (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_batches_registered_at ON app.batches (tenant_id, registered_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_batches_sending_lab ON app.batches (tenant_id, sending_lab_id) WHERE sending_lab_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE UNIQUE INDEX ix_batches_tenant_id_number ON app.batches (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_bookings_patient ON app.bookings (tenant_id, patient_id) WHERE patient_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_bookings_phlebotomist ON app.bookings (tenant_id, phlebotomist_user_id) WHERE phlebotomist_user_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_bookings_status_visit_at ON app.bookings (tenant_id, status, visit_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE UNIQUE INDEX ix_bookings_tenant_id_number ON app.bookings (tenant_id, number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_visit_lines_cancelled ON app.visit_lines (tenant_id, cancelled_at_utc) WHERE status = 'Cancelled';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_visit_lines_service ON app.visit_lines (tenant_id, service_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_visit_lines_tenant_id_parent_line_id ON app.visit_lines (tenant_id, parent_line_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_visit_lines_tube ON app.visit_lines (tenant_id, tube_id) WHERE tube_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_visit_lines_visit ON app.visit_lines (tenant_id, visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE UNIQUE INDEX ux_visit_preparation_answers_question ON app.visit_preparation_answers (tenant_id, visit_id, preparation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_visit_transfers_at ON app.visit_transfers (tenant_id, at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_visit_transfers_visit ON app.visit_transfers (tenant_id, visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_visit_tubes_visit ON app.visit_tubes (tenant_id, visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE UNIQUE INDEX ux_visit_tubes_sample_number ON app.visit_tubes (tenant_id, sample_number);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE UNIQUE INDEX ix_visits_tenant_id_accession ON app.visits (tenant_id, accession);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_visits_tenant_id_batch_id ON app.visits (tenant_id, batch_id) WHERE batch_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_visits_tenant_id_booking_id ON app.visits (tenant_id, booking_id) WHERE booking_id IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_visits_tenant_id_branch_id_registered_at_utc ON app.visits (tenant_id, branch_id, registered_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_visits_tenant_id_patient_id_registered_at_utc ON app.visits (tenant_id, patient_id, registered_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_visits_tenant_id_payer_id ON app.visits (tenant_id, payer_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    CREATE INDEX ix_visits_tenant_id_registered_at_utc ON app.visits (tenant_id, registered_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    ALTER TABLE app.batches ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.batches FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.batches
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.bookings ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.bookings FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.bookings
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.visits ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.visits FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.visits
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.visit_preparation_answers ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.visit_preparation_answers FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.visit_preparation_answers
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.visit_transfers ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.visit_transfers FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.visit_transfers
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.visit_tubes ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.visit_tubes FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.visit_tubes
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.visit_lines ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.visit_lines FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.visit_lines
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    -- Reporting (intake LIS-INT-REP-001, Release 1 scope: SA-03 platform activity). Appended by the integrator to the wave
    -- migration; plain SQL, safe to run twice (CREATE OR REPLACE, idempotent grants). Pattern of saas.client_usage_counts
    -- (PlatformAdmin.migration.sql): the function is created by the migration role (lis_owner, BYPASSRLS), runs as its owner
    -- (SECURITY DEFINER), answers counts only, and only to a session whose tenant is the platform tenant. The application
    -- never elevates for these figures.

    GRANT USAGE ON SCHEMA saas TO lis_app;

    -- SA-03 "served patients, tests and registrations per period" across the clients, one row per tenant with activity:
    --   registrations   = visits registered in [p_from, p_to)                       (app.visits, MODULES.md §5)
    --   served_patients = distinct patients of those visits
    --   tests           = results of those visits that are not cancelled             (app.results, MODULES.md §5; 0 before Results)
    -- Tables that do not exist yet answer nothing (no row), never an error; the statements are dynamic for that reason.
    CREATE OR REPLACE FUNCTION saas.client_activity_counts(p_from timestamptz, p_to timestamptz)
    RETURNS TABLE (tenant_id uuid, served_patients bigint, tests bigint, registrations bigint)
    LANGUAGE plpgsql
    STABLE
    SECURITY DEFINER
    SET search_path = pg_catalog, pg_temp
    AS $fn$
    #variable_conflict use_column
    BEGIN
        IF coalesce(current_setting('app.current_tenant', true), '') <> '00000000-0000-0000-0000-000000000001' THEN
            RAISE EXCEPTION 'saas.client_activity_counts is reserved to the platform tenant' USING ERRCODE = '42501';
        END IF;

        IF p_from IS NULL OR p_to IS NULL OR p_from >= p_to OR to_regclass('app.visits') IS NULL THEN
            RETURN;
        END IF;

        IF to_regclass('app.results') IS NULL THEN
            RETURN QUERY EXECUTE
                'SELECT v.tenant_id, count(DISTINCT v.patient_id), 0::bigint, count(*)
                 FROM app.visits v
                 WHERE v.registered_at_utc >= $1 AND v.registered_at_utc < $2
                 GROUP BY v.tenant_id'
                USING p_from, p_to;
        ELSE
            RETURN QUERY EXECUTE
                'SELECT v.tenant_id, count(DISTINCT v.patient_id), coalesce(sum(t.tests), 0)::bigint, count(*)
                 FROM app.visits v
                 CROSS JOIN LATERAL (
                     SELECT count(*) AS tests FROM app.results r
                     WHERE r.tenant_id = v.tenant_id AND r.visit_id = v.id AND r.status <> ''Cancelled''
                 ) t
                 WHERE v.registered_at_utc >= $1 AND v.registered_at_utc < $2
                 GROUP BY v.tenant_id'
                USING p_from, p_to;
        END IF;
    END
    $fn$;
    REVOKE ALL ON FUNCTION saas.client_activity_counts(timestamptz, timestamptz) FROM PUBLIC;
    GRANT EXECUTE ON FUNCTION saas.client_activity_counts(timestamptz, timestamptz) TO lis_app;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060006_Registration') THEN
    INSERT INTO ops.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261010060006_Registration', '9.0.20');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
    CREATE TABLE app.results (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        visit_id uuid NOT NULL,
        accession character varying(40) NOT NULL,
        line_id uuid NOT NULL,
        line_service_id uuid NOT NULL,
        service_id uuid NOT NULL,
        test_id uuid NOT NULL,
        test_type character varying(40) NOT NULL,
        is_hidden_helper boolean NOT NULL,
        tube_id uuid,
        clinical_group_id uuid NOT NULL,
        report_unit_key character varying(100) NOT NULL,
        patient_id uuid NOT NULL,
        branch_id uuid NOT NULL,
        payer_id uuid,
        urgent boolean NOT NULL,
        service_sequence integer NOT NULL,
        member_order integer NOT NULL,
        registered_at_utc timestamp with time zone NOT NULL,
        clock_start_utc timestamp with time zone NOT NULL,
        expected_at_utc timestamp with time zone NOT NULL,
        status character varying(40) NOT NULL,
        status_changed_at_utc timestamp with time zone NOT NULL,
        numeric_value numeric(18,6),
        text_value character varying(500),
        formatted_report jsonb,
        culture_report jsonb,
        unit character varying(50),
        decimals integer NOT NULL,
        range_low numeric(18,6),
        range_high numeric(18,6),
        range_panic_low numeric(18,6),
        range_panic_high numeric(18,6),
        range_reportable_low numeric(18,6),
        range_reportable_high numeric(18,6),
        range_text character varying(200),
        flag character varying(40),
        is_panic boolean NOT NULL,
        delta_flag boolean NOT NULL,
        delta_percent numeric(8,1),
        previous_result_id uuid,
        previous_value numeric(18,6),
        previous_verified_at_utc timestamp with time zone,
        delta_acknowledged_by_user_id uuid,
        delta_acknowledged_at_utc timestamp with time zone,
        delta_comment character varying(1000),
        source character varying(40) NOT NULL,
        analyser_id uuid,
        analyser_code character varying(32),
        analyser_flags character varying(200),
        measured_at_utc timestamp with time zone,
        entered_by_user_id uuid,
        entered_at_utc timestamp with time zone,
        reviewed_by_user_id uuid,
        reviewed_at_utc timestamp with time zone,
        verified_by_user_id uuid,
        verified_at_utc timestamp with time zone,
        verified_by_name character varying(200),
        printed_at_utc timestamp with time zone,
        last_printed_at_utc timestamp with time zone,
        last_printed_by_user_id uuid,
        print_count integer NOT NULL,
        last_print_list_number character varying(20),
        amendment_count integer NOT NULL,
        cancelled_at_utc timestamp with time zone,
        cancellation_reason character varying(1000),
        report_comment character varying(2000),
        internal_note character varying(2000),
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_results PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_results_flag_domain CHECK ("flag" IN ('Normal', 'Low', 'High', 'CriticalLow', 'CriticalHigh', 'Abnormal')),
        CONSTRAINT ck_results_source_domain CHECK ("source" IN ('Manual', 'Analyser')),
        CONSTRAINT ck_results_status_domain CHECK ("status" IN ('Ordered', 'Complete', 'Reviewed', 'Verified', 'Printed', 'Cancelled')),
        CONSTRAINT ck_results_test_type_domain CHECK ("test_type" IN ('Numeric', 'NonNumeric', 'OptionalList', 'RestrictedList', 'Formula', 'FormattedText', 'Culture'))
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
    CREATE TABLE app.result_log_entries (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        result_id uuid NOT NULL,
        kind character varying(40) NOT NULL,
        from_status character varying(40),
        to_status character varying(40),
        value_before character varying(1000),
        value_after character varying(1000),
        source character varying(40),
        analyser_id uuid,
        at_utc timestamp with time zone NOT NULL,
        by_user_id uuid,
        by_name character varying(200),
        detail character varying(1000),
        CONSTRAINT pk_result_log_entries PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_result_log_entries_from_status_domain CHECK ("from_status" IN ('Ordered', 'Complete', 'Reviewed', 'Verified', 'Printed', 'Cancelled')),
        CONSTRAINT ck_result_log_entries_kind_domain CHECK ("kind" IN ('Ordered', 'Value', 'Status', 'DeltaAcknowledged', 'Comment', 'Note', 'PatientMoved')),
        CONSTRAINT ck_result_log_entries_source_domain CHECK ("source" IN ('Manual', 'Analyser')),
        CONSTRAINT ck_result_log_entries_to_status_domain CHECK ("to_status" IN ('Ordered', 'Complete', 'Reviewed', 'Verified', 'Printed', 'Cancelled')),
        CONSTRAINT fk_result_log_entries_results_tenant_id_result_id FOREIGN KEY (tenant_id, result_id) REFERENCES app.results (tenant_id, id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
    CREATE INDEX ix_result_log_entries_tenant_id_result_id_at_utc ON app.result_log_entries (tenant_id, result_id, at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
    CREATE INDEX ix_results_tenant_id_accession ON app.results (tenant_id, accession);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
    CREATE INDEX ix_results_tenant_id_clinical_group_id_status ON app.results (tenant_id, clinical_group_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
    CREATE UNIQUE INDEX ix_results_tenant_id_line_id_test_id ON app.results (tenant_id, line_id, test_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
    CREATE INDEX ix_results_tenant_id_patient_id_test_id ON app.results (tenant_id, patient_id, test_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
    CREATE INDEX ix_results_tenant_id_registered_at_utc ON app.results (tenant_id, registered_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
    CREATE INDEX ix_results_tenant_id_report_unit_key ON app.results (tenant_id, report_unit_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
    CREATE INDEX ix_results_tenant_id_status_expected_at_utc ON app.results (tenant_id, status, expected_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
    CREATE INDEX ix_results_tenant_id_tube_id ON app.results (tenant_id, tube_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
    CREATE INDEX ix_results_tenant_id_visit_id ON app.results (tenant_id, visit_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
    ALTER TABLE app.results ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.results FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.results
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    ALTER TABLE app.result_log_entries ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.result_log_entries FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.result_log_entries
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
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
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010060455_Results') THEN
    INSERT INTO ops.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261010060455_Results', '9.0.20');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010061253_ResultLogSequence') THEN
    DROP INDEX app.ix_result_log_entries_tenant_id_result_id_at_utc;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010061253_ResultLogSequence') THEN
    ALTER TABLE app.result_log_entries ADD sequence integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010061253_ResultLogSequence') THEN
    ALTER TABLE app.result_log_entries DISABLE TRIGGER trg_result_log_entries_append_only;
    UPDATE app.result_log_entries AS e
    SET sequence = numbered.position
    FROM (
        SELECT tenant_id, id, row_number() OVER (PARTITION BY tenant_id, result_id ORDER BY at_utc, id) AS position
        FROM app.result_log_entries
    ) AS numbered
    WHERE e.tenant_id = numbered.tenant_id AND e.id = numbered.id;
    ALTER TABLE app.result_log_entries ENABLE TRIGGER trg_result_log_entries_append_only;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010061253_ResultLogSequence') THEN
    CREATE UNIQUE INDEX ix_result_log_entries_tenant_id_result_id_sequence ON app.result_log_entries (tenant_id, result_id, sequence);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261010061253_ResultLogSequence') THEN
    INSERT INTO ops.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261010061253_ResultLogSequence', '9.0.20');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261011124135_AppendOnlyTruncateGuard') THEN
    CREATE OR REPLACE FUNCTION audit.refuse_truncate() RETURNS trigger LANGUAGE plpgsql AS $fn$
    BEGIN
        RAISE EXCEPTION 'rows of %.% are append-only or never deleted: TRUNCATE is not allowed', TG_TABLE_SCHEMA, TG_TABLE_NAME
            USING ERRCODE = '42501';
    END
    $fn$;
    CREATE TRIGGER audit_trail_no_truncate BEFORE TRUNCATE ON audit.audit_trail FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER field_changes_no_truncate BEFORE TRUNCATE ON audit.field_changes FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER security_events_no_truncate BEFORE TRUNCATE ON audit.security_events FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER payments_no_truncate BEFORE TRUNCATE ON app.payments FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER refunds_no_truncate BEFORE TRUNCATE ON app.refunds FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER cash_closings_no_truncate BEFORE TRUNCATE ON app.cash_closings FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER agent_log_entries_no_truncate BEFORE TRUNCATE ON app.agent_log_entries FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER analyser_result_intake_no_truncate BEFORE TRUNCATE ON app.analyser_result_intake FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER patients_no_truncate BEFORE TRUNCATE ON app.patients FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER patient_documents_no_truncate BEFORE TRUNCATE ON app.patient_documents FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER visits_no_truncate BEFORE TRUNCATE ON app.visits FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER visit_lines_no_truncate BEFORE TRUNCATE ON app.visit_lines FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER visit_tubes_no_truncate BEFORE TRUNCATE ON app.visit_tubes FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER visit_preparation_answers_no_truncate BEFORE TRUNCATE ON app.visit_preparation_answers FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER visit_transfers_no_truncate BEFORE TRUNCATE ON app.visit_transfers FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER bookings_no_truncate BEFORE TRUNCATE ON app.bookings FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER batches_no_truncate BEFORE TRUNCATE ON app.batches FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER results_no_truncate BEFORE TRUNCATE ON app.results FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    CREATE TRIGGER result_log_entries_no_truncate BEFORE TRUNCATE ON app.result_log_entries FOR EACH STATEMENT EXECUTE FUNCTION audit.refuse_truncate();
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261011124135_AppendOnlyTruncateGuard') THEN
    INSERT INTO ops.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261011124135_AppendOnlyTruncateGuard', '9.0.20');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261011201519_GrantExportPermissions') THEN
    UPDATE app.roles AS r
    SET permission_keys = ARRAY(SELECT k FROM (SELECT DISTINCT unnest(r.permission_keys || ARRAY['organization.export']) AS k) AS d ORDER BY k COLLATE "C"),
        modified_at_utc = now(),
        modified_by_display_name = 'Migration GrantExportPermissions'
    WHERE r.permission_keys @> ARRAY['organization.view', 'organization.manage']
      AND NOT r.permission_keys @> ARRAY['organization.export'];

    UPDATE app.roles AS r
    SET permission_keys = ARRAY(SELECT k FROM (SELECT DISTINCT unnest(r.permission_keys || ARRAY['treasury.export']) AS k) AS d ORDER BY k COLLATE "C"),
        modified_at_utc = now(),
        modified_by_display_name = 'Migration GrantExportPermissions'
    WHERE r.permission_keys @> ARRAY['treasury.view', 'treasury.manage']
      AND NOT r.permission_keys @> ARRAY['treasury.export'];

    UPDATE app.roles AS r
    SET permission_keys = ARRAY(SELECT k FROM (SELECT DISTINCT unnest(r.permission_keys || c.keys) AS k) AS d ORDER BY k COLLATE "C"),
        modified_at_utc = now(),
        modified_by_display_name = 'Migration GrantExportPermissions'
    FROM (SELECT ARRAY[
        'audit.export', 'audit.view', 'automation.approve', 'automation.create', 'automation.edit',
        'automation.manage', 'automation.view', 'billing.approve', 'billing.cancel', 'billing.create', 'billing.edit',
        'billing.export', 'billing.manage', 'billing.view', 'catalogue.create', 'catalogue.edit', 'catalogue.export',
        'catalogue.manage', 'catalogue.view', 'claims.approve', 'claims.cancel', 'claims.create', 'claims.export',
        'claims.view', 'dashboard.view', 'files.create', 'files.manage', 'files.view', 'identity.create',
        'identity.edit', 'identity.manage', 'identity.view', 'integration.create', 'integration.edit',
        'integration.export', 'integration.manage', 'integration.view', 'notifications.manage', 'notifications.view',
        'organization.create', 'organization.edit', 'organization.export', 'organization.manage', 'organization.view',
        'patients.create', 'patients.edit', 'patients.export', 'patients.manage', 'patients.view',
        'patients.viewConfidential', 'platform.manage', 'platform.view', 'pricing.approve', 'pricing.create',
        'pricing.edit', 'pricing.export', 'pricing.manage', 'pricing.view', 'quality.create', 'quality.edit',
        'quality.manage', 'quality.view', 'registration.approve', 'registration.cancel', 'registration.create',
        'registration.edit', 'registration.export', 'registration.manage', 'registration.view', 'reporting.export',
        'reporting.view', 'results.approve', 'results.cancel', 'results.create', 'results.edit', 'results.export',
        'results.manage', 'results.view', 'samples.cancel', 'samples.create', 'samples.edit', 'samples.manage',
        'samples.view', 'settings.manage', 'settings.view', 'support.create', 'support.manage', 'support.view',
        'tenancy.create', 'tenancy.edit', 'tenancy.manage', 'tenancy.view', 'treasury.approve', 'treasury.cancel',
        'treasury.create', 'treasury.export', 'treasury.manage', 'treasury.view', 'wallet.create', 'wallet.manage',
        'wallet.view'
    ]::text[] AS keys) AS c
    WHERE r.tenant_id = '00000000-0000-0000-0000-000000000001'
      AND lower(r.name_en) = 'platform administrator'
      AND NOT r.permission_keys @> c.keys;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261011201519_GrantExportPermissions') THEN
    INSERT INTO ops.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261011201519_GrantExportPermissions', '9.0.20');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261012021635_LaboratoryLogo') THEN
    CREATE TABLE app.laboratory_logos (
        id uuid NOT NULL,
        tenant_id uuid NOT NULL,
        content_type character varying(50) NOT NULL,
        size_bytes bigint NOT NULL,
        storage_key character varying(400) NOT NULL,
        sha256 character(64) NOT NULL,
        version integer NOT NULL,
        is_active boolean NOT NULL,
        changed_by_user_id uuid NOT NULL,
        changed_at_utc timestamp with time zone NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by_user_id uuid,
        created_by_display_name character varying(200),
        modified_at_utc timestamp with time zone,
        modified_by_user_id uuid,
        modified_by_display_name character varying(200),
        CONSTRAINT pk_laboratory_logos PRIMARY KEY (tenant_id, id),
        CONSTRAINT ck_laboratory_logos_content_type CHECK (content_type IN ('image/png', 'image/jpeg', 'image/webp')),
        CONSTRAINT ck_laboratory_logos_size CHECK (size_bytes BETWEEN 1 AND 524288),
        CONSTRAINT ck_laboratory_logos_version CHECK (version >= 1)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261012021635_LaboratoryLogo') THEN
    CREATE UNIQUE INDEX ix_laboratory_logos_tenant_id ON app.laboratory_logos (tenant_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261012021635_LaboratoryLogo') THEN
    ALTER TABLE app.laboratory_logos ENABLE ROW LEVEL SECURITY;
    ALTER TABLE app.laboratory_logos FORCE ROW LEVEL SECURITY;
    CREATE POLICY tenant_isolation ON app.laboratory_logos
        USING (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        )
        WITH CHECK (
            tenant_id = NULLIF(current_setting('app.current_tenant', true), '')::uuid
            OR current_setting('app.bypass_rls', true) = 'on'
        );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261012021635_LaboratoryLogo') THEN
    INSERT INTO ops.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261012021635_LaboratoryLogo', '9.0.20');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261012135907_RemovePlatformKeysFromLaboratoryRoles') THEN
    UPDATE app.roles AS r
    SET permission_keys = ARRAY(
            SELECT k FROM (SELECT DISTINCT unnest(r.permission_keys) AS k) AS d
            WHERE split_part(k, '.', 1) NOT IN ('platform', 'tenancy')
            ORDER BY k COLLATE "C"),
        modified_at_utc = now(),
        modified_by_display_name = 'Migration RemovePlatformKeysFromLaboratoryRoles'
    WHERE r.tenant_id <> '00000000-0000-0000-0000-000000000001'
      AND EXISTS (SELECT 1 FROM unnest(r.permission_keys) AS k WHERE split_part(k, '.', 1) IN ('platform', 'tenancy'));

    UPDATE app.tenant_memberships AS m
    SET added_permission_keys = ARRAY(
            SELECT k FROM (SELECT DISTINCT unnest(m.added_permission_keys) AS k) AS d
            WHERE split_part(k, '.', 1) NOT IN ('platform', 'tenancy')
            ORDER BY k COLLATE "C"),
        removed_permission_keys = ARRAY(
            SELECT k FROM (SELECT DISTINCT unnest(m.removed_permission_keys) AS k) AS d
            WHERE split_part(k, '.', 1) NOT IN ('platform', 'tenancy')
            ORDER BY k COLLATE "C"),
        modified_at_utc = now(),
        modified_by_display_name = 'Migration RemovePlatformKeysFromLaboratoryRoles'
    WHERE m.tenant_id <> '00000000-0000-0000-0000-000000000001'
      AND EXISTS (SELECT 1 FROM unnest(m.added_permission_keys || m.removed_permission_keys) AS k WHERE split_part(k, '.', 1) IN ('platform', 'tenancy'));
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261012135907_RemovePlatformKeysFromLaboratoryRoles') THEN
    INSERT INTO ops.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261012135907_RemovePlatformKeysFromLaboratoryRoles', '9.0.20');
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261013141855_SpecialRightsExtended') THEN
    ALTER TABLE app.tenant_memberships ADD override_can_view_branch_collections boolean;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261013141855_SpecialRightsExtended') THEN
    ALTER TABLE app.tenant_memberships ADD override_demographics_edit_up_to character varying(16);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261013141855_SpecialRightsExtended') THEN
    ALTER TABLE app.tenant_memberships ADD override_report_days_limit integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261013141855_SpecialRightsExtended') THEN
    ALTER TABLE app.roles ADD special_can_view_branch_collections boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261013141855_SpecialRightsExtended') THEN
    ALTER TABLE app.roles ADD special_demographics_edit_up_to character varying(16) NOT NULL DEFAULT 'Complete';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261013141855_SpecialRightsExtended') THEN
    ALTER TABLE app.roles ADD special_report_days_limit integer;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261013141855_SpecialRightsExtended') THEN
    ALTER TABLE app.roles ADD CONSTRAINT ck_roles_special_demographics_edit_up_to_domain
        CHECK (special_demographics_edit_up_to IN ('Ordered', 'Complete', 'Reviewed', 'Verified', 'Printed'));
    ALTER TABLE app.tenant_memberships ADD CONSTRAINT ck_tenant_memberships_override_demographics_edit_up_to_domain
        CHECK (override_demographics_edit_up_to IS NULL OR override_demographics_edit_up_to IN ('Ordered', 'Complete', 'Reviewed', 'Verified', 'Printed'));
    ALTER TABLE app.roles ADD CONSTRAINT ck_roles_special_report_days_limit_range
        CHECK (special_report_days_limit IS NULL OR special_report_days_limit BETWEEN 0 AND 3650);
    ALTER TABLE app.tenant_memberships ADD CONSTRAINT ck_tenant_memberships_override_report_days_limit_range
        CHECK (override_report_days_limit IS NULL OR override_report_days_limit BETWEEN 0 AND 3650);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM ops.__ef_migrations_history WHERE "migration_id" = '20261013141855_SpecialRightsExtended') THEN
    INSERT INTO ops.__ef_migrations_history (migration_id, product_version)
    VALUES ('20261013141855_SpecialRightsExtended', '9.0.20');
    END IF;
END $EF$;
COMMIT;

