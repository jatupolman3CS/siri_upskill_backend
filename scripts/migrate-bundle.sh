#!/usr/bin/env bash
# P0-12 (docs/DATABASE.md, docs/DEPLOYMENT.md "CI/CD"): production schema changes are applied by
# running a self-contained EF Core migration bundle as its own deploy step — never via
# Database.Migrate() at app startup (Program.cs deliberately has no such call). This script just
# wraps `dotnet ef migrations bundle` with the right project wiring so that wiring lives in one
# place instead of being retyped in DEPLOYMENT.md / CI config / a developer's shell history.
#
# Usage:
#   backend/scripts/migrate-bundle.sh [-o OUTPUT] [-r RUNTIME]
#
#   -o OUTPUT   Path to the executable to produce (default: backend/dist/migrate-bundle).
#   -r RUNTIME  Target runtime identifier (default: linux-x64, matching the Contabo VPS/Docker
#               target in DEPLOYMENT.md). Override for a local smoke-test on another OS, e.g. -r
#               win-x64 — never needed for a real deploy.
#
# The produced executable does NOT embed a connection string (security.md: no real secrets in
# anything committed or built from committed sources). Run it on the target with either:
#   ./migrate-bundle --connection "<real connection string>"
# or by exporting ConnectionStrings__Default in the environment first — same convention
# ASP.NET Core configuration uses everywhere else in this app.
#
# The bundle also needs an appsettings.json next to it at run time (EF's own bundle tooling prints
# this reminder too) — Program.cs reads Identity:Jwt:SigningKey during startup unconditionally
# (even for a migrations-only run) and throws if it's missing/blank. The committed
# backend/src/Siri.Api/appsettings.json is enough for this — it carries only the non-secret
# CHANGE_ME_DEV_ONLY_ placeholder, which is non-blank and long enough to satisfy that check; the
# real secret the bundle needs is ConnectionStrings:Default, supplied separately as above.
#
# Per DEPLOYMENT.md: always back up the database immediately before running the produced bundle
# against a shared/production environment. This script only builds the bundle — it never runs it
# and never touches a database itself.

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
backend_dir="$(cd "$script_dir/.." && pwd)"

output="$backend_dir/dist/migrate-bundle"
runtime="linux-x64"

while getopts "o:r:h" opt; do
  case "$opt" in
    o) output="$OPTARG" ;;
    r) runtime="$OPTARG" ;;
    h)
      grep '^#' "$0" | cut -c3-
      exit 0
      ;;
    *)
      echo "Usage: $0 [-o OUTPUT] [-r RUNTIME]" >&2
      exit 1
      ;;
  esac
done

mkdir -p "$(dirname "$output")"

# A self-contained publish for a non-default RID needs that RID's packages restored first —
# `dotnet ef migrations bundle` does not restore on its own and fails with NETSDK1004 otherwise
# (found by actually running this script, not assumed). Restoring the whole solution rather than
# just the two projects passed below: Microsoft.EntityFrameworkCore.Design's bundle build walks the
# startup project's full reference graph (every Siri.Modules.* project), and each of those needs
# linux-x64 assets restored too.
echo "Restoring solution for runtime: $runtime"
dotnet restore "$backend_dir/SiriUpSkill.sln" --runtime "$runtime"

echo "Building self-contained migration bundle (runtime: $runtime) -> $output"

dotnet ef migrations bundle \
  --project "$backend_dir/src/Siri.Persistence/Siri.Persistence.csproj" \
  --startup-project "$backend_dir/src/Siri.Api/Siri.Api.csproj" \
  --context AppDbContext \
  --configuration Release \
  --target-runtime "$runtime" \
  --self-contained \
  --output "$output" \
  --force

echo "Bundle ready: $output"
echo "Copy appsettings.json alongside it, then run on the target with a real connection string, e.g.:"
echo "  cp \"$backend_dir/src/Siri.Api/appsettings.json\" \"$(dirname "$output")/\""
echo "  $output --connection \"<ConnectionStrings:Default value>\""
