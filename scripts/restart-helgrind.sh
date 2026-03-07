#!/usr/bin/env bash
set -euo pipefail

PROJECT_PATH="${1:-./Helgrind/Helgrind.csproj}"
RUN_MODE="${2:-build}"

pkill -f "Helgrind(.dll|.exe)?" || true

project_dir="$(dirname "$PROJECT_PATH")"
cd "$project_dir"

if [[ "$RUN_MODE" == "no-build" ]]; then
  dotnet run --no-build
else
  dotnet run
fi
