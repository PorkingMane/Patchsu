#!/usr/bin/env bash
# Builds the release binaries for Linux, macOS and Windows (x64 and arm64) into dist/:
#   ./build-release.sh            # version from main.go
#   ./build-release.sh 1.2        # this version
# Upload every file in dist/ to the release. Windows needs the .exe name, macOS picks darwin-arm64 (Apple Silicon)
# or darwin-amd64 (Intel).
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
version=${1:-$(sed -n 's/^var Version = "\(.*\)"/\1/p' main.go)}
go generate ./internal/bundle
rm -rf dist
mkdir -p dist
for t in linux/amd64 linux/arm64 darwin/amd64 darwin/arm64 windows/amd64 windows/arm64; do
  os=${t%/*} arch=${t#*/}
  out=dist/Patchsu-CLI-$os-$arch
  [ "$os" = windows ] && out+=.exe
  CGO_ENABLED=0 GOOS=$os GOARCH=$arch go build -trimpath -ldflags "-s -w -X main.Version=$version" -o "$out" .
  echo "Built $out"
done
