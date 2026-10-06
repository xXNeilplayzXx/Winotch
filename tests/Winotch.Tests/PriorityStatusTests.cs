namespace Winotch.Tests;

public class PriorityStatusTests
{
    private static PriorityStatusSnapshot Snap(int percent = 80, bool charging = false,
        string? wifi = "HomeNet", bool mic = false, bool cam = false) =>
        new(new BatteryInfo(percent, charging), new WifiStatus(wifi), mic, cam);

    [Fact]
    public void FirstSnapshot_QuietExceptCameraMicLowBattery()
    {
        var tracker = new PriorityStatusTracker();
        // A clean first snapshot is silent (no boot spam)...
        Assert.Null(tracker.Next(Snap()));
        // ...but if the very first poll already sees camera/mic/low battery,
        // those alerts fire immediately and drain one per Next call.
        Assert.Equal("Camera active", tracker.Next(Snap(cam: true, mic: true, percent: 10))?.Title);
        Assert.Equal("Microphone active", tracker.Next(Snap(cam: true, mic: true, percent: 10))?.Title);
        Assert.Equal("Low battery", tracker.Next(Snap(cam: true, mic: true, percent: 10))?.Title);
        Assert.Null(tracker.Next(Snap(cam: true, mic: true, percent: 10)));
    }

    [Fact]
    public void FirstSnapshot_ReportsActiveMic()
    {
        var tracker = new PriorityStatusTracker();
        var alert = tracker.Next(Snap(mic: true));
        Assert.NotNull(alert);
        Assert.Equal("Microphone active", alert.Title);
    }

    [Fact]
    public void ChargerConnect_RisingEdge_AlertWithFlourish()
    {
        var tracker = new PriorityStatusTracker();
        tracker.Next(Snap(percent: 42, charging: false));
        var alert = tracker.Next(Snap(percent: 42, charging: true));
        Assert.NotNull(alert);
        Assert.Equal("Charger connected", alert.Title);
        Assert.True(alert.ShowsChargingFlourish);
        Assert.Equal(42, alert.BatteryPercent);
    }

    [Fact]
    public void ChargerDisconnect_Alert()
    {
        var tracker = new PriorityStatusTracker();
        tracker.Next(Snap(percent: 60, charging: true));
        var alert = tracker.Next(Snap(percent: 60, charging: false));
        Assert.NotNull(alert);
        Assert.Equal("Charger disconnected", alert.Title);
    }

    [Fact]
    public void LowBattery_OnlyWhenCrossingThreshold()
    {
        var tracker = new PriorityStatusTracker();
        tracker.Next(Snap(percent: 30));
        var alert = tracker.Next(Snap(percent: 18));
        Assert.NotNull(alert);
        Assert.Equal("Low battery", alert.Title);
        Assert.Null(tracker.Next(Snap(percent: 15))); // still low, no new alert
    }

    [Fact]
    public void WifiLost_And_Reconnected()
    {
        var tracker = new PriorityStatusTracker();
        tracker.Next(Snap(wifi: "Office"));
        var lost = tracker.Next(Snap(wifi: null));
        Assert.NotNull(lost);
        Assert.Equal("Wi-Fi disconnected", lost.Title);
        var reconnected = tracker.Next(Snap(wifi: "Office2"));
        Assert.NotNull(reconnected);
        Assert.Equal("Wi-Fi reconnected", reconnected.Title);
    }

    [Fact]
    public void CameraRisingEdge_AlertOnce()
    {
        var tracker = new PriorityStatusTracker();
        tracker.Next(Snap());
        var on = tracker.Next(Snap(cam: true));
        Assert.NotNull(on);
        Assert.Equal("Camera active", on.Title);
        Assert.Null(tracker.Next(Snap(cam: true)));
    }

    [Fact]
    public void OnlyTheChargerAlerts_ArePillGlances()
    {
        // The pill answers what you notice happening and move on from; the rest
        // stay toasts, because they are worth a sentence.
        Assert.True(PriorityStatusAlert.ChargerConnected(42).PillGlance);
        Assert.True(PriorityStatusAlert.ChargerDisconnected(42).PillGlance);
        Assert.False(PriorityStatusAlert.LowBattery(15).PillGlance);
        Assert.False(PriorityStatusAlert.CameraActive().PillGlance);
        Assert.False(PriorityStatusAlert.MicrophoneActive().PillGlance);
        Assert.False(PriorityStatusAlert.WifiLost("HomeNet").PillGlance);
        Assert.False(PriorityStatusAlert.WifiReconnected("HomeNet").PillGlance);
    }

    [Fact]
    public void SameSnapshot_NoAlerts()
    {
        var tracker = new PriorityStatusTracker();
        tracker.Next(Snap());
        Assert.Null(tracker.Next(Snap()));
    }
}
