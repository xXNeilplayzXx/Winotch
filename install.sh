#!/bin/bash
set -e

# Winotch Linux installer
# Usage: curl -sL https://your-server.com/install.sh | sudo bash
# Or: sudo ./install.sh

# Configurable download URL (change this to your actual hosting location)
DOWNLOAD_URL="${WINOTCH_URL:-https://github.com/xXNeilplayzXx/Winotch/releases/latest/download/Winotch-linux-x64.tar.gz}"

INSTALL_DIR="/opt/winotch"
DESKTOP_FILE="/usr/share/applications/winotch.desktop"
ICON_FILE="/opt/winotch/winotch.png"
TARBALL="/tmp/Winotch-linux-x64.tar.gz"

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    echo "Please run as root (use sudo)"
    exit 1
fi

# Download or use local tarball
if [ -n "$1" ] && [ -f "$1" ]; then
    echo "Using local file: $1"
    cp "$1" "$TARBALL"
else
    echo "Downloading Winotch from $DOWNLOAD_URL..."
    if ! curl -sL "$DOWNLOAD_URL" -o "$TARBALL"; then
        echo "Error: Failed to download from $DOWNLOAD_URL"
        echo "You can provide a local file: sudo ./install.sh Winotch-linux-x64.tar.gz"
        exit 1
    fi
fi

echo "Installing Winotch..."

# Create install directory
mkdir -p "$INSTALL_DIR"

# Extract binary
echo "Extracting to $INSTALL_DIR..."
tar -xzf "$TARBALL" -C "$INSTALL_DIR"

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
