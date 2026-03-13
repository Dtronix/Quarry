#!/usr/bin/env bash
# Create the sample school database and scaffold Quarry schema files from it.
#
# This demonstrates the database-first workflow:
#   1. Create a real database with tables, FKs, indexes, and seed data
#   2. Run `quarry scaffold` to reverse-engineer schema .cs files + QuarryContext
#
# The generated files land in ./Schemas/ and are compiled by the Quarry source
# generator into entity types, a typed QueryBuilder API, and CRUD methods.
#
# Usage: ./create_and_scaffold.sh

set -euo pipefail

echo "=== Step 1: Creating sample SQLite database ==="
dotnet run -- create-db

echo ""
echo "=== Step 2: Scaffolding database into Quarry schema files ==="
dotnet run --project ../../Quarry.Tool -- scaffold \
    --dialect sqlite \
    --database school.db \
    --output ./Schemas \
    --namespace Scaffolding \
    --naming-style snakecase \
    --context SchoolDbContext \
    --non-interactive

echo ""
echo "=== Done ==="
echo "Generated schema files and QuarryContext in ./Schemas/"
echo "Run ./query.sh to see Quarry queries in action."
