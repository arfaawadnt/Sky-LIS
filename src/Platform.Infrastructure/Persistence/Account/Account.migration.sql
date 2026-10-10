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
