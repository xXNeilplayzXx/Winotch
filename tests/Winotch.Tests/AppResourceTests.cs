using Winotch;

namespace Winotch.Tests;

/// <summary>The tray/window icon is read back by its logical resource name, and
/// it is the only route to Settings when the notch itself is hidden — so the
/// embedded-resource name is part of the contract, not an implementation
/// detail. It silently regressed to a blank tray slot when it was lost.</summary>
public class AppResourceTests
{
    private const string TrayIconResource = "Winotch.Resources.WinotchTray.ico";

    [Fact]
    public void TrayIconIsEmbeddedUnderItsLogicalName()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(TrayIconResource);

        Assert.NotNull(stream);
        Assert.True(stream!.Length > 0, "the embedded tray icon is empty");
    }
}
