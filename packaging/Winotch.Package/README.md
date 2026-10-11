# MSIX package

This Windows Application Packaging Project wraps the existing WPF `Winotch` app. The portable self-contained EXE release remains unchanged.

## Build

Use Windows + Visual Studio 2022 with the **MSIX Packaging Tools** component (or a Visual Studio installation containing `Microsoft.DesktopBridge`). Build `Winotch.Package.wapproj` as **x64 / Release**. The project emits an unsigned MSIX under `AppPackages`; sign it with a certificate trusted by the target device before installation. The package identity/publisher must be set to the publisher certificate used for signing.

`dotnet` / `msbuild` on macOS or a Windows runner without Visual Studio's DesktopBridge targets cannot build this WAP project.

## Privacy-sensitive access

The package declares `userNotificationListener`, `appointmentsSystem`, and `runFullTrust`. `appointmentsSystem` is a restricted capability and may require partner approval for Store distribution; keep the manifest capability and publisher/distribution strategy aligned with actual Windows API access. Windows Notification Center history is opt-in in Winotch Settings and requires Windows' consent prompt. Native appointments are read-only and can be selected in the calendar widget. The listener API surfaces the current Windows toast list (not a permanent Windows history/archive), and Winotch's Clear action only clears its own stored view—it does not dismiss system notifications.

Microsoft's appointment API reference currently marks `AppointmentManager.RequestStoreAsync` and appointment enumeration as requiring the restricted `appointmentsSystem` capability, while its capability guide describes the general `appointments` capability as granting read access to the appointment store. This project declares `appointmentsSystem` because the API reference lists it for the calls used here. That restricted capability requires careful validation and may block Store submission absent approval. The app retains `.ics` import as a fallback.
