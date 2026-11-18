#!/usr/bin/env bash
# Run the core unit tests. Needs no game files.
# Golden replay test: set RELOCK_GOLDEN_PROFILE to a profile's saves dir (progress.save + history/).
set -euo pipefail
cd "$(dirname "$0")/.."
exec dotnet test tests/RelockAscensions.Core.Tests "$@"
