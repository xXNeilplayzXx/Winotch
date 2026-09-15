#!/bin/bash
set -e

# Winotch Linux installer
# Usage: curl -sL https://your-server.com/install.sh | sudo bash
# Or: sudo ./install.sh

# Optional override: WINOTCH_URL=/path/or/url/to/Winotch-vX.Y.Z-linux-x64.zip
DOWNLOAD_URL="${WINOTCH_URL:-}"

INSTALL_DIR="/opt/winotch"
DESKTOP_FILE="/usr/share/applications/winotch.desktop"
ICON_FILE="/opt/winotch/winotch.png"
ARCHIVE="/tmp/Winotch-linux-x64.zip"

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    echo "Please run as root (use sudo)"
    exit 1
fi

# Download or use a local archive
if [ -n "$1" ] && [ -f "$1" ]; then
    echo "Using local file: $1"
    cp "$1" "$ARCHIVE"
else
    # Every Winotch build is published as a GitHub *prerelease*, and
    # releases/latest deliberately ignores prereleases — resolve the newest
    # release through the API instead.
    if [ -z "$DOWNLOAD_URL" ]; then
        echo "Resolving the newest release..."
        DOWNLOAD_URL=$(curl -sL "https://api.github.com/repos/xXNeilplayzXx/Winotch/releases?per_page=1" \
            | grep -o 'https://[^"]*Winotch-v[0-9.]*-linux-x64\.zip' | head -1)
    fi
    if [ -z "$DOWNLOAD_URL" ]; then
        echo "Error: could not find a Winotch Linux release."
        echo "Set WINOTCH_URL to a release zip, or pass one as an argument."
        exit 1
    fi
    echo "Downloading Winotch from $DOWNLOAD_URL..."
    if ! curl -fsL "$DOWNLOAD_URL" -o "$ARCHIVE"; then
        echo "Error: Failed to download from $DOWNLOAD_URL"
        exit 1
    fi
fi

echo "Installing Winotch..."

# Create install directory
mkdir -p "$INSTALL_DIR"

# Extract binary
echo "Extracting to $INSTALL_DIR..."
if ! command -v unzip >/dev/null 2>&1; then
    echo "Error: unzip is required to extract the Winotch release"
    exit 1
fi
unzip -o -q "$ARCHIVE" -d "$INSTALL_DIR"
rm -f "$ARCHIVE"

# Make executable
chmod +x "$INSTALL_DIR/Winotch"

# Create symlink in PATH
ln -sf "$INSTALL_DIR/Winotch" /usr/local/bin/winotch

# Create desktop entry
echo "Creating desktop entry..."
cat > "$DESKTOP_FILE" << EOF
[Desktop Entry]
Name=Winotch
Comment=Dynamic Island for Linux
Exec=/usr/local/bin/winotch
Icon=$ICON_FILE
Terminal=false
Type=Application
Categories=Utility;
StartupWMClass=Winotch
EOF

# Create a simple icon (you can replace this with an actual icon file)
if [ ! -f "$ICON_FILE" ]; then
    echo "Creating placeholder icon..."
    cat > "$ICON_FILE" << 'EOF'
EOF
fi

# Update desktop database
update-desktop-database /usr/share/applications 2>/dev/null || true

echo ""
echo "Installation complete!"
echo ""
echo "You can now:"
echo "  - Run 'winotch' from the terminal"
echo "  - Find 'Winotch' in your application menu"
echo ""
echo "To uninstall:"
echo "  sudo rm -rf $INSTALL_DIR"
echo "  sudo rm $DESKTOP_FILE"
echo "  sudo rm /usr/local/bin/winotch"
