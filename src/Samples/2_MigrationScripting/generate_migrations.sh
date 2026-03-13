#!/usr/bin/env bash
# Generate the initial migration from the current schema definitions.
# Usage: ./migrate.sh

set -euo pipefail
dotnet run --project ../../Quarry.Tool -- migrate add InitialCreate -p .
