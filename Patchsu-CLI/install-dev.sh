#!/usr/bin/env bash
# Builds Patchsu-CLI and puts a copy into ../Patchsu, next to the patches folder.
# That copy uses ../Patchsu/patches automatically (no GitHub release needed), so you can run it from there:
#   cd ../Patchsu && ./Patchsu-CLI
# The binary also carries the patches and the logo (go generate copies them into internal/bundle), so a copy
# taken anywhere else still builds with the patches it was made with.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
go generate ./internal/bundle
go build -ldflags "-X main.Version=1.0" -o ../Patchsu/Patchsu-CLI .
echo "Built ../Patchsu/Patchsu-CLI (uses ../Patchsu/patches; built-in copy of them too)"
