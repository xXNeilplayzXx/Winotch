# Winotch Windows installer
# Usage: irm https://raw.githubusercontent.com/xXNeilplayzXx/Winotch/main/install.ps1 | iex

$ErrorActionPreference = "Stop"

$InstallDir = "$env:ProgramFiles\Winotch"
$ExePath = "$InstallDir\Winotch.exe"

Write-Host "Installing Winotch..." -ForegroundColor Cyan

# Every Winotch build is published as a GitHub *prerelease*, and
# releases/latest deliberately ignores prereleases — resolving the newest
# release through the API is the only way to reach the current build.
$DownloadUrl = $env:WINOTCH_URL
if (-not $DownloadUrl) {
    Write-Host "Resolving the newest release..."
    try {
        $releases = Invoke-RestMethod -Uri "https://api.github.com/repos/xXNeilplayzXx/Winotch/releases?per_page=1" -UseBasicParsing
        $asset = $releases[0].assets | Where-Object { $_.name -like "Winotch-v*-x64.zip" } | Select-Object -First 1
        if (-not $asset) { throw "release $($releases[0].tag_name) has no Windows zip" }
        $DownloadUrl = $asset.browser_download_url
    } catch {
        Write-Error "Could not find a Winotch release to download. Set `$env:WINOTCH_URL to a release zip and run again. ($_)"
        exit 1
    }
}

# Create install directory
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
}

# Download binary
$TempArchive = "$env:TEMP\Winotch-win-x64.zip"
Write-Host "Downloading from $DownloadUrl..."
try {
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $TempArchive -UseBasicParsing
} catch {
    Write-Error "Failed to download from $DownloadUrl"
    exit 1
}

# Extract
Write-Host "Extracting to $InstallDir..."
Expand-Archive -Path $TempArchive -DestinationPath $InstallDir -Force
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
