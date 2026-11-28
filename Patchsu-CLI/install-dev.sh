#!/usr/bin/env bash
# Builds Patchsu-CLI into this folder (Patchsu-CLI/Patchsu-CLI). It finds ../Patchsu/patches automatically
# (no GitHub release needed), so you can run it from here:
#   ./Patchsu-CLI
# The binary also carries the patches and the logo (go generate copies them into internal/bundle), so a copy
# taken anywhere else still builds with the patches it was made with.
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
go generate ./internal/bundle
go build -ldflags "-X main.Version=1.3" -o Patchsu-CLI .
echo "Built Patchsu-CLI/Patchsu-CLI (uses ../Patchsu/patches; built-in copy of them too)"
