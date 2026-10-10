#!/usr/bin/env bash
# Creates the private test database of a build slot (lis_<slot>) on the private PostgreSQL 17 instance (127.0.0.1:5433)
# and runs deploy/postgres/schema.sql on it. Migrations are applied by the test fixtures themselves.
#   bash scripts/new-slot-database.sh <slot> [--reset]   (--reset drops the database first, e.g. after a migration is regenerated)
set -euo pipefail
slot="${1:?slot name required}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
set -a
# shellcheck disable=SC1091
source "$root/deploy/postgres/.env"
set +a
psql="C:/Program Files/PostgreSQL/17/bin/psql.exe"
db="lis_${slot}"
export PGPASSWORD="$LIS_PG_SUPERUSER_PASSWORD"
if [ "${2:-}" = "--reset" ]; then
  "$psql" -v ON_ERROR_STOP=1 -q -h 127.0.0.1 -p 5433 -U postgres -d postgres -c "DROP DATABASE IF EXISTS $db WITH (FORCE);"
  echo "$db dropped"
fi
if "$psql" -h 127.0.0.1 -p 5433 -U postgres -d postgres -Atc "select 1 from pg_database where datname = '$db'" | grep -q 1; then
  echo "$db exists"
else
  "$psql" -v ON_ERROR_STOP=1 -h 127.0.0.1 -p 5433 -U postgres -d postgres -c "CREATE DATABASE $db OWNER lis_owner ENCODING 'UTF8' TEMPLATE template0;" -c "REVOKE ALL ON DATABASE $db FROM PUBLIC;" -c "GRANT CONNECT, TEMPORARY ON DATABASE $db TO lis_app;"
  echo "$db created"
fi
export PGPASSWORD="$LIS_OWNER_PASSWORD"
"$psql" -v ON_ERROR_STOP=1 -q -h 127.0.0.1 -p 5433 -U lis_owner -d "$db" -f "$root/deploy/postgres/schema.sql"
echo "$db ready"
