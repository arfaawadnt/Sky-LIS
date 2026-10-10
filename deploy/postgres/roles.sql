-- deploy/postgres/roles.sql — run once per server as a superuser, connected to the "postgres" database:
--   psql -v ON_ERROR_STOP=1 -v owner_pw='<secret>' -v app_pw='<secret>' -f roles.sql
--
-- Two roles (01 §1 rule 5, §4.6):
--   lis_owner  owns the database and runs migrations; BYPASSRLS so backfills never need app.bypass_rls (C-10)
--   lis_app    the runtime role; NOBYPASSRLS, not a table owner, never SUPERUSER (the start-up guard refuses otherwise)
-- Names follow the LIS reserved resource table (CLAUDE.md §2).

CREATE ROLE lis_owner LOGIN PASSWORD :'owner_pw' BYPASSRLS   NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;
CREATE ROLE lis_app   LOGIN PASSWORD :'app_pw'   NOBYPASSRLS NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT;

CREATE DATABASE lis OWNER lis_owner ENCODING 'UTF8' TEMPLATE template0;

REVOKE ALL ON DATABASE lis FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE lis TO lis_app;
