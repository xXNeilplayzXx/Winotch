#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/src/Winotch"
OUTPUT_DIR="$SCRIPT_DIR/dist"
DOTNET_INSTALL_DIR="${DOTNET_INSTALL_DIR:-$HOME/.dotnet}"
DOTNET_VERSION="8.0.404"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

info() { echo -e "${GREEN}[INFO]${NC} $*"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $*"; }
error() { echo -e "${RED}[ERROR]${NC} $*"; exit 1; }

# --- Install dotnet SDK if missing ---
ensure_dotnet() {
    if command -v dotnet &>/dev/null; then
        info "dotnet already available: $(dotnet --version)"
        return
    fi

    if [ -x "$DOTNET_INSTALL_DIR/dotnet" ]; then
        export PATH="$DOTNET_INSTALL_DIR:$PATH"
        info "dotnet found at $DOTNET_INSTALL_DIR: $(dotnet --version)"
        return
    fi

    info "Installing .NET SDK $DOTNET_VERSION..."
    curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --version "$DOTNET_VERSION" --install-dir "$DOTNET_INSTALL_DIR"
    rm -f /tmp/dotnet-install.sh
    export PATH="$DOTNET_INSTALL_DIR:$PATH"
    info "Installed dotnet $(dotnet --version)"
}

# --- Detect platform ---
detect_platform() {
    local os arch rid
    case "$(uname -s)" in
        Linux)  os="linux" ;;
        Darwin) os="osx" ;;
        MINGW*|MSYS*|CYGWIN*) os="win" ;;
        *) error "Unsupported OS: $(uname -s)" ;;
    esac

    case "$(uname -m)" in
        x86_64|amd64)  arch="x64" ;;
        aarch64|arm64)  arch="arm64" ;;
        *) error "Unsupported architecture: $(uname -m)" ;;
    esac

    rid="${os}-${arch}"
    echo "$rid"
}

# --- Build ---
build() {
    local rid="$1"
    local selfcontained="${2:-false}"

    info "Building Winotch for $rid (self-contained=$selfcontained)..."

    rm -rf "$OUTPUT_DIR"
    mkdir -p "$OUTPUT_DIR"

    local publish_args=(
        publish
        "$PROJECT_DIR/Winotch.csproj"
        -c Release
        -r "$rid"
        -o "$OUTPUT_DIR"
        --self-contained "$selfcontained"
        -p:PublishSingleFile=true
        -p:PublishTrimmed=false
        -p:IncludeNativeLibrariesForSelfExtract=false
    )

    if [ "$selfcontained" = "true" ]; then
        publish_args+=(-p:PublishSingleFile=false)
    fi

    dotnet "${publish_args[@]}"

    info "Build complete. Output: $OUTPUT_DIR"
}

# --- Package as tarball/zip ---
package() {
    local rid="$1"
    local version
    version=$(grep -oP '<Version>\K[^<]+' "$PROJECT_DIR/Winotch.csproj")

    local ext="tar.gz"
    local archive="Winotch-${version}-${rid}.${ext}"
    local archive_path="$SCRIPT_DIR/$archive"

    info "Packaging $archive..."

    # Make binary executable
    chmod +x "$OUTPUT_DIR/Winotch" 2>/dev/null || true

    tar -czf "$archive_path" -C "$OUTPUT_DIR" .

    info "Package created: $archive_path"
    info "Size: $(du -h "$archive_path" | cut -f1)"
    echo ""
    info "To install:"
    info "  mkdir -p \$HOME/.local/opt/winotch"
    info "  tar -xzf $archive -C \$HOME/.local/opt/winotch"
    info "  \$HOME/.local/opt/winotch/Winotch"
}

# --- Main ---
main() {
    local selfcontained=false
    local rid=""

    while [[ $# -gt 0 ]]; do
        case "$1" in
            --self-contained) selfcontained=true; shift ;;
            --rid) rid="$2"; shift 2 ;;
            -h|--help)
                echo "Usage: $0 [--self-contained] [--rid <runtime-id>]"
                echo ""
                echo "Options:"
                echo "  --self-contained   Bundle .NET runtime (larger, no dotnet needed)"
                echo "  --rid <rid>        Target runtime (default: auto-detect)"
                echo "                     Examples: linux-x64, linux-arm64, win-x64, osx-x64"
                exit 0
                ;;
            *) error "Unknown option: $1" ;;
        esac
    done

    ensure_dotnet

    if [ -z "$rid" ]; then
        rid=$(detect_platform)
        info "Auto-detected runtime: $rid"
    fi

    build "$rid" "$selfcontained"
    package "$rid"
}

main "$@"
