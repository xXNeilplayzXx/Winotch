# Winotch Windows installer
# Usage: irm https://raw.githubusercontent.com/xXNeilplayzXx/Winotch/main/install.ps1 | iex

$ErrorActionPreference = "Stop"

$DownloadUrl = if ($env:WINOTCH_URL) { $env:WINOTCH_URL } else { "https://github.com/xXNeilplayzXx/Winotch/releases/latest/download/Winotch-win-x64.tar.gz" }
$InstallDir = "$env:ProgramFiles\Winotch"
$ExePath = "$InstallDir\Winotch.exe"

Write-Host "Installing Winotch..." -ForegroundColor Cyan

# Create install directory
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

# Download binary
$TempArchive = "$env:TEMP\Winotch-win-x64.tar.gz"
Write-Host "Downloading from $DownloadUrl..."
try {
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $TempArchive -UseBasicParsing
} catch {
    Write-Error "Failed to download from $DownloadUrl"
    exit 1
}

# Extract
Write-Host "Extracting to $InstallDir..."
tar -xzf $TempArchive -C $InstallDir
Remove-Item $TempArchive -Force

# Add to PATH if not already there
$CurrentPath = [Environment]::GetEnvironmentVariable("Path", "Machine")
if ($CurrentPath -notlike "*$InstallDir*") {
    Write-Host "Adding to PATH..."
    [Environment]::SetEnvironmentVariable("Path", "$CurrentPath;$InstallDir", "Machine")
}

# Create Start Menu shortcut
$ShortcutPath = "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Winotch.lnk"
Write-Host "Creating Start Menu shortcut..."
$WshShell = New-Object -ComObject WScript.Shell
$Shortcut = $WshShell.CreateShortcut($ShortcutPath)
$Shortcut.TargetPath = $ExePath
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.Description = "Dynamic Island for Windows"
$Shortcut.Save()

Write-Host ""
Write-Host "Installation complete!" -ForegroundColor Green
Write-Host ""
Write-Host "You can now:"
Write-Host "  - Run 'Winotch' from the command line"
Write-Host "  - Find 'Winotch' in your Start Menu"
Write-Host ""
Write-Host "To uninstall:"
Write-Host "  - Remove the folder: $InstallDir"
Write-Host "  - Delete the shortcut: $ShortcutPath"
Write-Host "  - Remove from PATH (optional)"
