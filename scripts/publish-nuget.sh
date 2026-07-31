#!/usr/bin/env bash
# Publish MinimalSerializers.Json to nuget.org using credentials from .env
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

ENV_FILE="${ENV_FILE:-$ROOT/.env}"
if [[ ! -f "$ENV_FILE" ]]; then
  echo "error: missing $ENV_FILE" >&2
  echo "Copy the example and add your key:" >&2
  echo "  cp .env.example .env" >&2
  echo "  # edit .env and set NUGET_API_KEY=..." >&2
  exit 1
fi

# Load KEY=value pairs from .env (ignores blank lines and # comments).
set -a
# shellcheck disable=SC1090
source <(grep -E '^[A-Za-z_][A-Za-z0-9_]*=' "$ENV_FILE" | sed 's/\r$//')
set +a

PACKAGE_ID="${PACKAGE_ID:-MinimalSerializers.Json}"
CONFIGURATION="${CONFIGURATION:-Release}"
NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
OUTPUT_DIR="${OUTPUT_DIR:-$ROOT/artifacts/packages}"
SKIP_TESTS="${SKIP_TESTS:-false}"
PUSH="${PUSH:-true}"

if [[ -z "${NUGET_API_KEY:-}" ]]; then
  echo "error: NUGET_API_KEY is empty in $ENV_FILE" >&2
  echo "Create a key at https://www.nuget.org/account/apikeys and put it in .env" >&2
  exit 1
fi

# Version: explicit PACKAGE_VERSION, else latest v* tag, else VersionPrefix/1.0.0
if [[ -z "${PACKAGE_VERSION:-}" ]]; then
  if git describe --tags --exact-match HEAD >/dev/null 2>&1; then
    PACKAGE_VERSION="$(git describe --tags --exact-match HEAD)"
    PACKAGE_VERSION="${PACKAGE_VERSION#v}"
  elif git describe --tags --match 'v*' --abbrev=0 >/dev/null 2>&1; then
    PACKAGE_VERSION="$(git describe --tags --match 'v*' --abbrev=0)"
    PACKAGE_VERSION="${PACKAGE_VERSION#v}"
  else
    PACKAGE_VERSION="1.0.0"
  fi
fi

echo "Publishing ${PACKAGE_ID} ${PACKAGE_VERSION} (${CONFIGURATION})"
echo "Source: ${NUGET_SOURCE}"
echo "Output: ${OUTPUT_DIR}"

mkdir -p "$OUTPUT_DIR"

dotnet restore MinimalSerializers.slnx
dotnet build MinimalSerializers.slnx -c "$CONFIGURATION" \
  -p:Version="$PACKAGE_VERSION" \
  -p:PackageVersion="$PACKAGE_VERSION"

if [[ "$SKIP_TESTS" != "true" ]]; then
  dotnet test tests/MinimalSerializers.Json.Tests/MinimalSerializers.Json.Tests.csproj \
    -c "$CONFIGURATION" --no-build
  dotnet test tests/MinimalSerializers.Json.Package.Tests/MinimalSerializers.Json.Package.Tests.csproj \
    -c "$CONFIGURATION" --no-build
fi

dotnet pack src/MinimalSerializers.Json/MinimalSerializers.Json.csproj \
  -c "$CONFIGURATION" \
  -o "$OUTPUT_DIR" \
  --no-build \
  -p:Version="$PACKAGE_VERSION" \
  -p:PackageVersion="$PACKAGE_VERSION"

shopt -s nullglob
packages=("$OUTPUT_DIR"/*.nupkg)
if (( ${#packages[@]} == 0 )); then
  echo "error: no nupkg files found in $OUTPUT_DIR" >&2
  exit 1
fi

echo "Packed:"
ls -la "$OUTPUT_DIR"

if [[ "$PUSH" != "true" ]]; then
  echo "PUSH=false; skipping nuget push"
  exit 0
fi

dotnet nuget push "$OUTPUT_DIR"/*.nupkg \
  --api-key "$NUGET_API_KEY" \
  --source "$NUGET_SOURCE" \
  --skip-duplicate

echo "Publish complete: https://www.nuget.org/packages/${PACKAGE_ID}/${PACKAGE_VERSION}"
