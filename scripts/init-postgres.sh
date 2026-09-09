#!/usr/bin/env bash
# Runs only when Docker initializes an empty PostgreSQL volume.
set -euo pipefail
psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --set=ON_ERROR_STOP=1 --set=app_password="$SIRI_DATABASE_PASSWORD" <<'SQL'
CREATE ROLE siriupskill_app LOGIN PASSWORD :'app_password' NOSUPERUSER NOCREATEDB NOCREATEROLE;
ALTER DATABASE "SIRIUPSKILL" OWNER TO siriupskill_app;
CREATE EXTENSION IF NOT EXISTS pg_trgm;
SQL