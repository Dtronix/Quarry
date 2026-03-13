#!/usr/bin/env bash
# Run queries against the scaffolded school database using Quarry's QueryBuilder.
#
# Prerequisites: Run ./create_and_scaffold.sh first to create the database.
#
# Usage: ./query.sh

set -euo pipefail
dotnet run -- query
