#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_USE_POLLING_FILE_WATCHER=1
# The test owns a temporary SQLite file, starts Kestrel, uses real HTTP,
# kills/restarts the process, verifies history, and removes its database.
dotnet test --filter 'FullyQualifiedName~ServerSmokeTests' --logger 'console;verbosity=normal'
