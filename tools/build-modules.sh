#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${1:-Release}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_DIR="$SCRIPT_DIR"
BACKEND_ROOT="$ROOT_DIR/../CitadelX.Backend"
BACKEND_PACKAGES_DIR="$BACKEND_ROOT/modules/packages"
BACKEND_MODULES_DIR="$BACKEND_ROOT/modules"
if [[ -d "$BACKEND_ROOT" ]]; then
  mkdir -p "$BACKEND_PACKAGES_DIR"
  mkdir -p "$BACKEND_MODULES_DIR"
fi

build_module() {
  local name="$1"
  local backend_proj="$2"
  local backend_dll="$3"
  local node_proj="$4"
  local node_dll="$5"

  echo "Building $name (Backend)..."
  dotnet build "$ROOT_DIR/$backend_proj" -c "$CONFIGURATION"

  echo "Building $name (Node)..."
  dotnet build "$ROOT_DIR/$node_proj" -c "$CONFIGURATION"

  local backend_out="$ROOT_DIR/$(dirname "$backend_proj")/bin/$CONFIGURATION/net8.0"
  local node_out="$ROOT_DIR/$(dirname "$node_proj")/bin/$CONFIGURATION/net8.0"
  local backend_path="$backend_out/$backend_dll"
  local node_path="$node_out/$node_dll"

  if [[ ! -f "$backend_path" ]]; then
    echo "Backend DLL not found: $backend_path" >&2
    exit 1
  fi
  if [[ ! -f "$node_path" ]]; then
    echo "Node DLL not found: $node_path" >&2
    exit 1
  fi

  local zip_path="$DIST_DIR/$name.zip"
  rm -f "$zip_path"

  if command -v zip >/dev/null 2>&1; then
    (cd "$ROOT_DIR" && zip -j "$zip_path" "$backend_path" "$node_path" >/dev/null)
  else
    echo "zip is not installed. Please install zip to build module archives." >&2
    exit 1
  fi

  echo "Packaged: $zip_path"
  if [[ -d "$BACKEND_ROOT" ]]; then
    cp "$zip_path" "$BACKEND_PACKAGES_DIR/$(basename "$zip_path")"
    echo "Copied to backend packages: $BACKEND_PACKAGES_DIR/$(basename "$zip_path")"
    cp "$backend_path" "$BACKEND_MODULES_DIR/$(basename "$backend_path")"
    echo "Copied backend module DLL: $BACKEND_MODULES_DIR/$(basename "$backend_path")"
  fi
}

build_module "Singbox" \
  "SingboxModule/CitadelX.SingboxModule.csproj" \
  "CitadelX.SingboxModule.dll" \
  "SingboxNodeModule/SingboxNodeModule.csproj" \
  "CitadelX.SingboxNodeModule.dll"

build_module "SingboxExtended" \
  "SingboxExtendedModule/CitadelX.SingboxExtendedModule.csproj" \
  "CitadelX.SingboxExtendedModule.dll" \
  "SingboxExtendedNodeModule/SingboxExtendedNodeModule.csproj" \
  "CitadelX.SingboxExtendedNodeModule.dll"
