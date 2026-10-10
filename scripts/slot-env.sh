#!/usr/bin/env bash
# Environment for one build slot: a private obj/bin tree and a private test database, so several people (or agents)
# can build and test the same working tree at once. Usage (Git Bash):
#   source scripts/slot-env.sh <slot>        # e.g. source scripts/slot-env.sh cat
# Afterwards: dotnet build Platform.sln -c Release / dotnet test tests/<Suite> -c Release --no-build
# compile under .local/build/<slot>/ and the database tests use lis_<slot> on the private PostgreSQL (127.0.0.1:5433).
# Create the database once with scripts/new-slot-database.sh <slot>.
slot="${1:?slot name required}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
set -a
# shellcheck disable=SC1091
source "$root/deploy/postgres/.env"
set +a
export LisBuildSlot="$slot"
export LIS_TEST_OWNER_CONNECTION="Host=127.0.0.1;Port=5433;Database=lis_${slot};Username=lis_owner;Password=${LIS_OWNER_PASSWORD}"
export LIS_TEST_APP_CONNECTION="Host=127.0.0.1;Port=5433;Database=lis_${slot};Username=lis_app;Password=${LIS_APP_PASSWORD}"
export LIS_DEVTOOLS_CONNECTION="$LIS_TEST_OWNER_CONNECTION"
unset LIS_PG_SUPERUSER_PASSWORD LIS_OWNER_PASSWORD LIS_APP_PASSWORD
echo "slot $slot: build output .local/build/$slot, database lis_$slot"
