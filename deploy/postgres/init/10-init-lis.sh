#!/bin/bash
# Runs once, on the first start of the lis-postgres container (docker-entrypoint-initdb.d), as the superuser.
set -euo pipefail

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
     -v owner_pw="$LIS_OWNER_PASSWORD" -v app_pw="$LIS_APP_PASSWORD" \
     -f /lis/roles.sql

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname lis -f /lis/schema.sql
