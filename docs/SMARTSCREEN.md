# SmartScreen & antivirus warnings — why they happen and how to run Winotch

Winotch's Windows build is **not code-signed**, so Windows and some antivirus products flag it as "unrecognized" — not because it's malicious, but because they've never seen its fingerprint before. Every self-built indie app without a paid certificate gets this. This page explains exactly what to do, and what signing would cost if you want the warnings gone for good.

## Why the warning appears

Windows SmartScreen builds a "reputation" score for every executable on the internet. An exe with no digital signature and no download history starts at zero reputation, so the first users see:

> *Windows protected your PC — Microsoft Defender SmartScreen prevented an unrecognized app from starting.*

That's a reputation prompt, **not** a virus detection. Separately, Defender or third-party antivirus may occasionally flag unsigned Rust/Tauri exes as heuristic false positives (`Wacatac`, `Program:Win32/...` etc. — generic heuristic names, not a confirmed finding).

## Run it anyway (SmartScreen)

1. Double-click `winotch.exe`.
2. When the blue **"Windows protected your PC"** dialog appears, click **More info**.
3. The **Run anyway** button appears — click it. You only have to do this once per file; Windows remembers.

## Unblock a downloaded file (if the dialog keeps reappearing)

Windows marks files downloaded from the internet with a "Zone.Identifier" that re-triggers SmartScreen on every launch.

**File Properties way:** right-click `winotch.exe` → **Properties** → check **Unblock** at the bottom → **OK**. Do the same for the two DLLs if you extracted them from a zip.

**PowerShell way** (unblocks every file in the folder at once):

```powershell
cd C:\path\to\Winotch-Windows
Get-ChildItem | Unblock-File
```

## If your antivirus quarantines it

1. Open **Windows Security → Virus & threat protection → Protection history** (or your AV's quarantine list).
2. Find the Winotch entry → **Restore** / **Allow on device**.
3. Add the Winotch folder to exclusions: **Windows Security → Virus & threat protection → Manage settings → Exclusions → Add an exclusion → Folder**.
4. Still convinced it's a false positive? Submit it to Microsoft: **https://www.microsoft.com/wdsi/filesubmission** (select "Software developer"). Microsoft typically removes false detections within 24–48 hours.

To confirm nothing is tampered with, the v0.2.0 exe SHA-256 is published with each release — compare with `certutil -hashfile winotch.exe SHA256` (Command Prompt) or `Get-FileHash winotch.exe` (PowerShell).

## Making the warnings go away permanently (code signing)

Signing gives the exe a verified publisher name ("Verified publisher: <you>") and SmartScreen stops prompting after reputation builds. Options, cheapest first:

| Option | What you need | Cost (2026) | Effect |
|---|---|---|---|
| **Self-signed cert** | PowerShell, 5 minutes | Free | No SmartScreen *reputation*, but installs cleanly on machines where you've imported the cert. Good for your own PCs. |
| **Azure Trusted Signing** | Azure account + identity validation | ~$9.99/month | Real SmartScreen-recognized signature; Microsoft's recommended budget option for indies. |
| **OV code-signing cert** | Certificate authority (Certum, Sectigo, SSL.com); identity check | ~$70–200/yr | Removes "unknown publisher". SmartScreen reputation still builds gradually. |
| **EV code-signing cert** | CA with extended validation (business entity required) | ~$250–400/yr | Instant SmartScreen reputation. |

### Quick self-signed route (for personal machines)

```powershell
# 1. Create a self-signed cert (run once, in PowerShell as your user)
$cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=Neil Sohal" -CertStoreLocation Cert:\CurrentUser\My

# 2. Trust it on this machine (admin PowerShell)
Export-Certificate -Cert $cert -FilePath C:\temp\winotch.cer
Import-Certificate -FilePath C:\temp\winotch.cer -CertStoreLocation Cert:\LocalMachine\TrustedPublisher

# 3. Sign the exe
Set-AuthenticodeSignature -FilePath C:\path\to\winotch.exe -Certificate $cert -TimestampServer "http://timestamp.digicert.com"
```

### Wiring signing into the release build (when you buy a cert)

1. Export the cert as `certificate.pfx` with a password.
2. Add secrets to the GitHub repo (**Settings → Secrets and variables → Actions**): `WINDOWS_CERTIFICATE` (base64 of the pfx: `base64 -i certificate.pfx \| pbcopy` on macOS) and `WINDOWS_CERTIFICATE_PASSWORD`.
3. Uncomment a signing step in CI (see `.github/workflows/win-build.yml` when re-added):

```yaml
- name: Sign exe
  if: env.WINDOWS_CERTIFICATE != ''
  shell: pwsh
  run: |
    $pfx = [IO.Path]::GetTempFileName()
    [IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:WINDOWS_CERTIFICATE))
    signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f $pfx /p "$env:WINDOWS_CERTIFICATE_PASSWORD" winotch.exe
    Remove-Item $pfx
```

4. `signtool` ships with the [Windows SDK](https://developer.microsoft.com/windows/downloads/windows-sdk/); on GitHub runners it's preinstalled at `C:\Program Files (x86)\Windows Kits\10\bin\`.

Until a cert is in place, the **More info → Run anyway** step above is the expected one-time friction for every new user.
