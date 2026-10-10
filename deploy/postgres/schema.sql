-- deploy/postgres/schema.sql — run once as lis_owner (or a superuser), connected to the "lis" database, before the first migration:
--   psql -v ON_ERROR_STOP=1 -d lis -f schema.sql
--
-- Schemas and grants of 01 §4.1 / §4.6. Tables are created by the EF Core migrations (as lis_owner); the default
-- privileges below give lis_app exactly the rights the matrix allows on every table those migrations create:
--   app, read, ops  : SELECT, INSERT, UPDATE, DELETE
--   identity        : SELECT, INSERT, UPDATE
--   audit           : SELECT, INSERT (append-only)
--   saas            : nothing by default (tenants SELECT by id is granted by its own migration)

CREATE SCHEMA IF NOT EXISTS identity AUTHORIZATION lis_owner;
CREATE SCHEMA IF NOT EXISTS app      AUTHORIZATION lis_owner;
CREATE SCHEMA IF NOT EXISTS audit    AUTHORIZATION lis_owner;
CREATE SCHEMA IF NOT EXISTS saas     AUTHORIZATION lis_owner;
CREATE SCHEMA IF NOT EXISTS read     AUTHORIZATION lis_owner;
CREATE SCHEMA IF NOT EXISTS ops      AUTHORIZATION lis_owner;

REVOKE CREATE ON SCHEMA public FROM PUBLIC;

GRANT USAGE ON SCHEMA identity, app, audit, read, ops TO lis_app;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA app, read, ops TO lis_app;
GRANT SELECT, INSERT, UPDATE         ON ALL TABLES IN SCHEMA identity        TO lis_app;
GRANT SELECT, INSERT                 ON ALL TABLES IN SCHEMA audit           TO lis_app;
REVOKE UPDATE, DELETE, TRUNCATE      ON ALL TABLES IN SCHEMA audit           FROM lis_app;
GRANT USAGE ON ALL SEQUENCES IN SCHEMA app, read, ops, identity TO lis_app;

ALTER DEFAULT PRIVILEGES FOR ROLE lis_owner IN SCHEMA app, read, ops GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO lis_app;
ALTER DEFAULT PRIVILEGES FOR ROLE lis_owner IN SCHEMA identity      GRANT SELECT, INSERT, UPDATE         ON TABLES TO lis_app;
ALTER DEFAULT PRIVILEGES FOR ROLE lis_owner IN SCHEMA audit         GRANT SELECT, INSERT                 ON TABLES TO lis_app;
ALTER DEFAULT PRIVILEGES FOR ROLE lis_owner IN SCHEMA app, read, ops, identity GRANT USAGE ON SEQUENCES TO lis_app;
