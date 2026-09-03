using SkiaSharp;

namespace Winotch.Engine;

/// <summary>The notch itself: a spring-driven capsule that morphs between a
/// collapsed pill and an expanded panel, plus the toast stack that hangs below
/// it. Rendering is clipped to the island silhouette, so nothing can paint
/// outside the rounded shape — no window resizing, no corner artifacts.</summary>
public sealed class IslandShell
{
    private const double MiniHeight = 44;
    private const double MiniWidthMin = 150;
    private const double ExpandedWidth = 398;
    private const double NotchTopMini = -13;
    private const double NotchTopPanel = -6;
    private const double IslandTopMini = 10;
    private const double IslandTopPanel = 12;

    private readonly SettingsService _settings;
    private readonly SecondOrderVec2 _size = new(new Vec2(MiniWidthMin, MiniHeight), 3.2, 0.8, 0);
    private readonly List<Toast> _toasts = new();
    private readonly List<HitButton> _buttons = new();
    private readonly Dictionary<string, SKBitmap> _thumbs = new();

    // Data the host window feeds in.
    public MediaSnapshot? Media { get; private set; }
    public BatteryInfo Battery { get; private set; } = new(100, false);
    public IReadOnlyList<ClipboardEntry> ClipboardItems { get; private set; } = Array.Empty<ClipboardEntry>();
    public FocusTimerState Focus { get; private set; } = FocusTimerState.Stopped;
    public FocusTimerSettings FocusPreset { get; private set; } =
        new(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), false);
    public WeatherSnapshot? Weather { get; private set; }
    public SystemStatsSnapshot SystemStats { get; private set; } = new(0, 0, 0, 0);

    // Actions the host window wires up.
    public event Action? OpenSettingsRequested;
    public event Action? QuitRequested;
    public event Action? MediaPrevRequested;
    public event Action? MediaPlayPauseRequested;
    public event Action? MediaNextRequested;
    public event Action<long>? CopyClipboardEntryRequested;
    public event Action<long>? DeleteClipboardEntryRequested;
    public event Action? ClearClipboardRequested;
    public event Action? NotesEditRequested;
    public event Action? WeatherEditRequested;
    public event Action? LauncherEditRequested;
    public event Action? SystemMetricsEditRequested;
    public event Action<LauncherApp>? LaunchAppRequested;

    // Live geometry.
    public double CanvasWidth { get; private set; }
    public double CanvasHeight { get; private set; }
    private double _x, _y, _w, _h;
    private double _cursorX, _cursorY;
    private bool _wantExpanded;
    private double _blend;
    private bool _paused;
    private bool _addMenuOpen;

    public IslandShell(SettingsService settings) => _settings = settings;

    public bool Paused
    {
        get => _paused;
        set
        {
            _paused = value;
            _wantExpanded = false;
            _blend = 0;
        }
    }

    public bool AnyHoverButton { get; private set; }

    // -------------------------------------------------------------- data set

    public void SetCanvas(double widthDip, double heightDip)
    {
        CanvasWidth = widthDip;
        CanvasHeight = heightDip;
    }

    public void SetCursor(double xDip, double yDip)
    {
        _cursorX = xDip;
        _cursorY = yDip;
    }

    public void SetMedia(MediaSnapshot? media) => Media = media;
    public void SetBattery(BatteryInfo battery) => Battery = battery;
    public void SetFocus(FocusTimerState focus) => Focus = focus;
    public void SetWeather(WeatherSnapshot? weather) => Weather = weather;
    public void SetSystemStats(SystemStatsSnapshot stats) => SystemStats = stats;

    public void SetClipboard(IReadOnlyList<ClipboardEntry> items)
    {
        ClipboardItems = items;
        _thumbs.Clear();
    }

    public void ShowToast(ToastSpec spec)
    {
        _toasts.Add(new Toast(spec));
        if (_toasts.Count > 4) _toasts.RemoveAt(0);
    }

    public void ShowStatusToast(PriorityStatusAlert alert) =>
        ShowToast(new ToastSpec(alert.Icon, alert.Title, alert.Body,
            alert.ShowsChargingFlourish ? 4.2 : 4.8,
            alert.BatteryPercent, alert.ShowsChargingFlourish));

    // ---------------------------------------------------------------- tick

    public void Tick(double deltaTime)
    {
        if (_paused)
        {
            _toasts.Clear();
            return;
        }

        if (Focus.IsActive)
        {
            Focus = Focus.AdvanceTo(DateTimeOffset.UtcNow, OnPhaseComplete);
        }

        for (var i = _toasts.Count - 1; i >= 0; i--)
        {
            _toasts[i].Tick(deltaTime);
            if (_toasts[i].Expired) _toasts.RemoveAt(i);
        }

        var targetBlend = _wantExpanded ? 1.0 : 0.0;
        _blend += (targetBlend - _blend) * Math.Min(1, deltaTime * 18);
        if (_blend < 0.002) _blend = 0;
        if (_blend > 0.998) _blend = 1;

        UpdateGeometry(deltaTime);
    }

    private void OnPhaseComplete(FocusTimerCompletion completion)
    {
        if (completion.Kind == FocusTimerCompletionKind.FocusComplete)
        {
            ShowToast(new ToastSpec("", "Focus complete", $"Nice — take a {MinutesText(completion.NextPhaseDuration)} break.", 5.5));
        }
        else if (completion.Kind == FocusTimerCompletionKind.BreakComplete)
        {
            var text = Focus.AutoCycle ? "Break over — back to focus." : "Break over — timer stopped.";
            ShowToast(new ToastSpec("", "Break over", text, 5.5));
        }
    }

    private static string MinutesText(TimeSpan duration) =>
        duration.TotalMinutes >= 60 ? $"{duration.TotalHours:0.#} h" : $"{(int)duration.TotalMinutes} min";

    // ------------------------------------------------------------- geometry

    private void UpdateGeometry(double deltaTime)
    {
        var settings = _settings.Current;
        var expanded = _blend > 0.5;

        var miniTarget = MiniTarget();
        var expandedW = Math.Min(ExpandedWidth, Math.Max(120, CanvasWidth - 16));
        var expandedH = ExpandedHeight(settings);

        var target = expanded ? new Vec2(expandedW, expandedH) : new Vec2(miniTarget.Width, MiniHeight);
        var size = _size.Step(deltaTime, target);
        _w = size.X;
        _h = size.Y;

        var notch = settings.General.Style == NotchStyle.Notch;
        var targetTop = notch
            ? (expanded ? NotchTopPanel : NotchTopMini)
            : (expanded ? IslandTopPanel : IslandTopMini);
        _y += (targetTop - _y) * Math.Min(1, deltaTime * 16);
        _x = (CanvasWidth - _w) / 2;

        // Hover toggles expansion with hysteresis.
        var rect = new SKRect((float)_x, (float)_y, (float)(_x + _w), (float)(_y + _h));
        var pad = expanded ? 8 : 12;
        var hoverRect = new SKRect(rect.Left - pad, rect.Top - pad, rect.Right + pad, rect.Bottom + pad);
        var cursor = new SKPoint((float)_cursorX, (float)_cursorY);
        if (!_wantExpanded && hoverRect.Contains(cursor) && _blend < 0.5)
        {
            _wantExpanded = true;
        }
        else if (_wantExpanded && !hoverRect.Contains(cursor))
        {
            _wantExpanded = false;
            _addMenuOpen = false;
        }
    }

    private (double Width, double Height) MiniTarget()
    {
        var total = 0.0;
        foreach (var item in BuildMiniRow()) total += item.Width + 8;
        var w = Math.Clamp(total, MiniWidthMin, Math.Max(MiniWidthMin, CanvasWidth - 16));
        return (w, MiniHeight);
    }

    private double ExpandedHeight(WinotchSettings settings)
    {
        var h = 16 + 46.0; // top pad + header row
        foreach (var id in settings.Widgets.Enabled)
        {
            h += 12 + WidgetCardHeight(id, settings);
        }
        h += 12; // bottom pad
        return Math.Min(h, Math.Max(120, CanvasHeight - 24));
    }

    private double WidgetCardHeight(string id, WinotchSettings settings) => id switch
    {
        "clock" => 88,
        "system" => 38 + Math.Max(1, settings.Widgets.SystemMetrics.Count) * 24,
        "media" => Media is null ? 0 : 80,
        "notes" => 104,
        "weather" => 100,
        "launcher" => 40 + LauncherRows(settings) * 58,
        "focus" => settings.Features.FocusTimerEnabled ? 132 : 0,
        "clipboard" => settings.Features.ClipboardHistoryEnabled ? 38 + Math.Clamp(ClipboardItems.Count, 1, 5) * 36 : 0,
        _ => 0,
    };

    private static int LauncherRows(WinotchSettings settings) =>
        Math.Max(1, (int)Math.Ceiling(Math.Min(settings.Widgets.LauncherApps.Count, 12) / 4.0));

    public bool IsInteractiveAt(double xDip, double yDip)
    {
        if (_paused) return false;
        var expanded = _blend > 0.5;
        var m = expanded ? 6 : 10;
        var rect = new SKRect((float)(_x - m), (float)(_y - m),
            (float)(_x + _w + m), (float)(_y + _h + m));
        if (rect.Contains(new SKPoint((float)xDip, (float)yDip))) return true;

        if (_toasts.Count > 0)
        {
            var (tx, ty, tw) = ToastAnchor();
            if (xDip >= tx - 8 && xDip <= tx + tw + 8 && yDip >= ty - 4 && yDip <= ty + 160)
            {
                return true;
            }
        }
        return false;
    }

    public void Click(double xDip, double yDip)
    {
        var point = new Vec2(xDip, yDip);
        for (var i = _buttons.Count - 1; i >= 0; i--)
        {
            if (_buttons[i].Contains(point))
            {
                _buttons[i].OnClick();
                return;
            }
        }
        // Clicked a blank spot with the add menu open — dismiss it.
        _addMenuOpen = false;
    }

    // -------------------------------------------------------------- drawing

    public void Draw(SKCanvas canvas)
    {
        if (_paused) return;

        var expanded = _blend > 0.5;
        var radius = expanded ? 22.0 : MiniHeight / 2;
        var rect = new SKRect((float)_x, (float)_y, (float)(_x + _w), (float)(_y + _h));

        // Shadow pass (fill translucent dark + drop-shadow filter spread).
        using (var shadowFilter = SKImageFilter.CreateDropShadow(
            expanded ? 4 : 1, expanded ? 8 : 3,
            expanded ? 26 : 15, expanded ? 26 : 15, SKColors.Black))
        using (var shadow = new SKPaint
        {
            Color = new SKColor(0, 0, 0, (byte)(expanded ? 200 : 130)),
            IsAntialias = true,
        })
        {
            shadow.ImageFilter = shadowFilter;
            canvas.DrawRoundRect(rect, (float)radius, (float)radius, shadow);
        }

        // Island body: clip to silhouette, fill, crossfade content layers.
        canvas.Save();
        using (var path = new SKPath())
        {
            path.AddRoundRect(rect, (float)radius, (float)radius);
            canvas.ClipPath(path, SKClipOperation.Intersect, antialias: true);

            using (var body = ShellUi.Gradient(rect.Top, rect.Bottom))
            {
                canvas.DrawRect(rect, body);
            }

            if (_blend < 0.999)
            {
                using var miniLayer = new SKPaint
                {
                    Color = new SKColor(255, 255, 255, (byte)((1 - _blend) * 255)),
                };
                canvas.SaveLayer(miniLayer);
                DrawMini(canvas, rect);
                canvas.Restore();
            }
            if (_blend > 0.001)
            {
                using var panelLayer = new SKPaint
                {
                    Color = new SKColor(255, 255, 255, (byte)(_blend * 255)),
                };
                canvas.SaveLayer(panelLayer);
                DrawPanel(canvas, rect);
                canvas.Restore();
            }

            using (var topGlow = new SKPaint { Color = new SKColor(255, 255, 255, 14), IsAntialias = true })
            {
                canvas.DrawRect(rect.Left + 3, rect.Top + 1, Math.Max(0, rect.Width - 6), 1, topGlow);
            }
        }
        canvas.Restore();

        ShellUi.StrokeRound(canvas, rect.Left, rect.Top, rect.Width, rect.Height,
            (float)radius, 1f, ShellUi.Fade(Col.White, 0.08));

        DrawToasts(canvas);
    }

    // -------------------------------------------------------------- pill

    private enum MiniKind { Led, Clock, Battery, Media, Focus }

    private readonly record struct MiniItem(MiniKind Kind, double Width);

    private List<MiniItem> BuildMiniRow()
    {
        var items = new List<MiniItem> { new(MiniKind.Led, 10) };
        var clock = ClockText(_settings.Current);
        items.Add(new MiniItem(MiniKind.Clock, TextKit.Measure(clock, 15, TextKit.Semibold)));
        if (BatteryText().Length > 0)
        {
            items.Add(new MiniItem(MiniKind.Battery, 16 + TextKit.Measure(BatteryText(), 10.5f, TextKit.Monospace)));
        }
        if (Media is { Playing: true })
        {
            items.Add(new MiniItem(MiniKind.Media, 22 + TextKit.Measure(MiniTrackLabel(), 11.5f, TextKit.Semibold)));
        }
        if (Focus.IsActive)
        {
            items.Add(new MiniItem(MiniKind.Focus, TextKit.Measure(FocusLabel(), 11.5f, TextKit.Semibold) + 20));
        }
        return items;
    }

    private void DrawMini(SKCanvas canvas, SKRect rect)
    {
        var cy = rect.Top + rect.Height / 2;
        var items = BuildMiniRow();
        var total = 0.0;
        foreach (var item in items) total += item.Width + 8;
        var x = rect.Left + (rect.Width - (float)total) / 2;
        var clock = ClockText(_settings.Current);

        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case MiniKind.Led:
                    ShellUi.FillCircle(canvas, x + 5, cy, 3.4f, LedColor());
                    break;
                case MiniKind.Clock:
                    TextKit.Draw(canvas, clock, x, cy - 11, 15, ShellUi.Text, TextKit.Semibold);
                    break;
                case MiniKind.Battery:
                    DrawBattery(canvas, x + 8, (float)cy, BatteryText());
                    break;
                case MiniKind.Media:
                    var trackBudget = Math.Max(40, rect.Right - x - 26);
                    var track = ShellUi.Ellipsize(MiniTrackLabel(), trackBudget, 11.5f, TextKit.Semibold);
                    ShellUi.DrawGlyph(canvas, "\uE768", x + 11, cy, 12, ShellUi.Accent);
                    TextKit.Draw(canvas, track, x + 20, cy - 8.5f, 11.5f, ShellUi.Text, TextKit.Semibold);
                    break;
                case MiniKind.Focus:
                    var chipW = (float)item.Width - 20;
                    var isBreak = Focus.Phase == FocusTimerPhase.Break;
                    var color = isBreak ? ShellUi.Ok : ShellUi.Accent;
                    FillRound(canvas, x + 10 - chipW / 2, cy - 11, chipW, 22, 11, ShellUi.Fade(color, 0.16));
                    TextKit.DrawCentered(canvas, FocusLabel(), x + 10, cy, 11.5f, color, TextKit.Semibold);
                    break;
            }
            x += (float)item.Width + 8;
        }
    }

    private void DrawBattery(SKCanvas canvas, float cx, float cy, string text)
    {
        const float w = 12f;
        const float h = 6.5f;
        var left = cx - w / 2;
        var top = cy - h / 2;
        ShellUi.StrokeRound(canvas, left, top, w, h, 1.5f, 1f, ShellUi.Fade(ShellUi.Muted, 0.95));
        using (var tip = ShellUi.Fill(ShellUi.Fade(ShellUi.Muted, 0.95)))
        {
            canvas.DrawRect(left + w + 0.8f, top + h / 2 - 1, 1.3f, 2, tip);
        }
        var fill = Math.Clamp(Battery.Percent, 0, 100) / 100f;
        if (fill > 0.02)
        {
            var color = Battery.IsCharging ? ShellUi.Ok : fill <= 0.2 ? ShellUi.Danger : ShellUi.Text;
            FillRound(canvas, left + 1.7f, top + 1.7f, Math.Max(0.8f, (w - 3.4f) * fill), h - 3.4f, 0.7f, color);
        }
        if (Battery.IsCharging)
        {
            ShellUi.DrawGlyph(canvas, "\uE945", cx, cy, 8.5f, ShellUi.Ok);
        }
        TextKit.Draw(canvas, text, cx + 11, cy - 8, 10.5f, ShellUi.Fade(ShellUi.Muted, 0.95), TextKit.Monospace);
    }

    // ------------------------------------------------------------- panel

    private void DrawPanel(SKCanvas canvas, SKRect rect)
    {
        _buttons.Clear();
        if (!_wantExpanded) _addMenuOpen = false;

        var pad = 14f;
        var left = rect.Left + pad;
        var right = rect.Right - pad;
        var y = rect.Top + 14f;
        var settings = _settings.Current;

        // Header: date left, clock center, actions right.
        TextKit.Draw(canvas, DateText(settings), left, y + 8, 10.5f, ShellUi.Fade(ShellUi.Muted, 1), TextKit.Monospace);
        var clock = ClockText(settings);
        TextKit.DrawCentered(canvas, clock, rect.Left + rect.Width / 2, y + 21, 21, ShellUi.Text, TextKit.Semibold);

        var headerCy = y + 21;
        DrawIconButton(canvas, right - 24, headerCy, "\uE711", QuitRequested);
        DrawIconButton(canvas, right - 52, headerCy, "\uE713", OpenSettingsRequested);
        DrawIconButton(canvas, right - 80, headerCy, "\uE710", () => _addMenuOpen = !_addMenuOpen);

        var yCursor = y + 48f;

        // Widget cards in persisted order. The add menu floats above them.
        foreach (var id in settings.Widgets.Enabled)
        {
            var cardH = (float)WidgetCardHeight(id, settings);
            if (cardH <= 0) continue;
            var top = yCursor + 4;
            DrawCard(canvas, left, top, rect.Width - pad * 2, cardH);
            DrawCardHeader(canvas, left, top, rect.Width - pad * 2, id);
            DrawWidgetBody(canvas, left, top, rect.Width - pad * 2, id, settings);
            yCursor = top + cardH + 12;
        }

        if (_addMenuOpen)
        {
            DrawAddMenu(canvas, right, y + 48);
        }
    }

    // ------------------------------------------------------ widget dispatch

    private void DrawWidgetBody(SKCanvas canvas, float left, float top, float width, string id, WinotchSettings settings)
    {
        switch (id)
        {
            case "clock": DrawClockWidget(canvas, left, top, width); break;
            case "system": DrawSystemWidget(canvas, left, top, width, settings); break;
            case "media": DrawMediaWidget(canvas, left, top, width); break;
            case "notes": DrawNotesWidget(canvas, left, top, width, settings); break;
            case "weather": DrawWeatherWidget(canvas, left, top, width, settings); break;
            case "launcher": DrawLauncherWidget(canvas, left, top, width, settings); break;
            case "focus": DrawFocusWidget(canvas, left, top, width); break;
            case "clipboard": DrawClipboardWidget(canvas, left, top, width); break;
        }
    }

    private void DrawCardHeader(SKCanvas canvas, float left, float top, float width, string id)
    {
        var right = left + width;
        var enabled = _settings.Current.Widgets.Enabled;
        var index = enabled.IndexOf(id);
        var cy = top + 16;

        TextKit.Draw(canvas, WidgetSettings.Labels[id].ToUpperInvariant(), left + 12, top + 10, 8.5f,
            ShellUi.Fade(ShellUi.Muted, 1), TextKit.Monospace);

        // ✕ remove
        DrawIconButton(canvas, right - 10, cy, "\uE711", () => RemoveWidget(id));
        var cx = right - 10;

        // ⚙ edit (configurable cards only)
        Action? edit = id switch
        {
            "notes" => () => NotesEditRequested?.Invoke(),
            "weather" => () => WeatherEditRequested?.Invoke(),
            "launcher" => () => LauncherEditRequested?.Invoke(),
            "system" => () => SystemMetricsEditRequested?.Invoke(),
            _ => null,
        };
        if (edit is not null)
        {
            DrawIconButton(canvas, cx - 28, cy, "\uE713", edit);
        }

        // ↓ move down
        var downX = cx - (edit is not null ? 56 : 28);
        if (index >= 0 && index < enabled.Count - 1)
        {
            DrawIconButton(canvas, downX, cy, "\uE70D", () => MoveWidget(id, 1));
        }
        else
        {
            ShellUi.DrawGlyph(canvas, "\uE70D", downX, cy, 12.5f, ShellUi.Fade(ShellUi.Text, 0.22));
        }

        // ↑ move up
        var upX = downX - 28;
        if (index > 0)
        {
            DrawIconButton(canvas, upX, cy, "\uE70E", () => MoveWidget(id, -1));
        }
        else
        {
            ShellUi.DrawGlyph(canvas, "\uE70E", upX, cy, 12.5f, ShellUi.Fade(ShellUi.Text, 0.22));
        }
    }

    private void DrawAddMenu(SKCanvas canvas, float right, float top)
    {
        var available = AvailableWidgets();
        var rowH = 30f;
        var width = 172f;
        var height = Math.Max(rowH * available.Count + 8, 34);
        var x = right - width;

        FillRound(canvas, x, top, width, height, 10, ShellUi.Fade(Col.Rgba(0x13, 0x15, 0x1B), 0.98));
        ShellUi.StrokeRound(canvas, x, top, width, height, 10, 1f, ShellUi.Fade(ShellUi.Stroke, 0.6));

        if (available.Count == 0)
        {
            TextKit.Draw(canvas, "All widgets added", x + 14, top + 12, 11, ShellUi.Fade(ShellUi.Muted, 1));
            return;
        }

        var rowY = top + 4f;
        foreach (var id in available)
        {
            var hovering = _cursorX >= x && _cursorX <= x + width && _cursorY >= rowY && _cursorY <= rowY + rowH;
            if (hovering)
            {
                FillRound(canvas, x + 4, rowY + 2, width - 8, rowH - 4, 7, ShellUi.CardHover);
            }
            TextKit.Draw(canvas, WidgetSettings.Labels[id], x + 14, rowY + 9, 12,
                hovering ? ShellUi.Text : ShellUi.Fade(ShellUi.Text, 0.85f));
            var type = id;
            RegisterButton(new SKRect(x, rowY, x + width, rowY + rowH), () => { AddWidget(type); _addMenuOpen = false; });
            rowY += rowH;
        }
    }

    private List<string> AvailableWidgets()
    {
        var settings = _settings.Current;
        return WidgetSettings.Known
            .Where(id => !settings.Widgets.Enabled.Contains(id))
            .Where(id => id != "focus" || settings.Features.FocusTimerEnabled)
            .Where(id => id != "clipboard" || settings.Features.ClipboardHistoryEnabled)
            .ToList();
    }

    // ------------------------------------------------------ widget bodies

    private void DrawClockWidget(SKCanvas canvas, float left, float top, float width)
    {
        var centerX = left + width / 2;
        TextKit.DrawCentered(canvas, ClockText(_settings.Current), centerX, top + 52, 30, ShellUi.Text, TextKit.Semibold);
        TextKit.DrawCentered(canvas, DateTime.Now.ToString("dddd, MMMM d"), centerX, top + 76, 12.5f, ShellUi.Fade(ShellUi.Muted, 1));
    }

    private void DrawSystemWidget(SKCanvas canvas, float left, float top, float width, WinotchSettings settings)
    {
        var metrics = settings.Widgets.SystemMetrics;
        var rows = new List<(string Label, string Value, SKTypeface? Face, string? Icon)>();
        if (metrics.Cpu) rows.Add(("CPU", $"{SystemStats.CpuPercent:0}%", null, null));
        if (metrics.Memory) rows.Add(("MEM", $"{SystemStats.MemoryPercent:0}%", null, null));
        if (metrics.Battery) rows.Add(("BAT", $"{Battery.Percent}%", null, Battery.IsCharging ? "\uE945" : null));
        if (metrics.Network) rows.Add(("NET", $"{SystemStats.NetDownKbps:0} \u2193  {SystemStats.NetUpKbps:0} \u2191 KB/s", TextKit.Emoji, null));
        if (rows.Count == 0) rows.Add(("—", "No metrics enabled", null, null));

        var rowY = top + 34f;
        foreach (var (label, value, face, icon) in rows)
        {
            TextKit.Draw(canvas, label, left + 14, rowY + 5, 10.5f, ShellUi.Fade(ShellUi.Muted, 1), TextKit.Monospace);
            var valueFace = face ?? TextKit.Monospace;
            var valueWidth = TextKit.Measure(value, 10.5f, valueFace);
            TextKit.Draw(canvas, value, left + width - 14 - valueWidth - (icon is null ? 0 : 16),
                rowY + 5, 10.5f, ShellUi.Text, valueFace);
            if (icon is not null)
            {
                ShellUi.DrawGlyph(canvas, icon, left + width - 14 - 9, rowY + 12, 11.5f, ShellUi.Ok);
            }
            rowY += 24;
        }
    }

    private void DrawMediaWidget(SKCanvas canvas, float left, float top, float width)
    {
        if (Media is not { } media) return;
        var cardH = 80f;
        var right = left + width;
        TextKit.Draw(canvas,
            ShellUi.Ellipsize(media.Title.Length > 0 ? media.Title : "Untitled", width - 130, 12.5f, TextKit.Semibold),
            left + 12, top + 33, 12.5f, ShellUi.Text, TextKit.Semibold);
        TextKit.Draw(canvas,
            ShellUi.Ellipsize(media.Artist.Length > 0 ? media.Artist : "Unknown artist", width - 130, 10f, TextKit.Regular),
            left + 12, top + 50, 10f, ShellUi.Fade(ShellUi.Muted, 1));
        DrawTimeline(canvas, left + 12, top + cardH - 9, width - 24, media);

        var buttonCy = top + cardH / 2;
        var buttonRight = right - 16;
        DrawIconButton(canvas, buttonRight - 66, buttonCy, "\uE892", MediaPrevRequested);
        DrawIconButton(canvas, buttonRight - 34, buttonCy, media.Playing ? "\uE769" : "\uE768", MediaPlayPauseRequested);
        DrawIconButton(canvas, buttonRight - 2, buttonCy, "\uE893", MediaNextRequested);
    }

    private void DrawNotesWidget(SKCanvas canvas, float left, float top, float width, WinotchSettings settings)
    {
        var text = settings.Widgets.Notes;
        var x = left + 14;
        if (string.IsNullOrWhiteSpace(text))
        {
            TextKit.Draw(canvas, "Jot something down — tap this card to open the editor.",
                x, top + 46, 11.5f, ShellUi.Fade(ShellUi.Muted, 0.9f));
        }
        else
        {
            var lines = text.Replace("\r", "").Split('\n');
            var shown = Math.Min(3, lines.Length);
            for (var i = 0; i < shown; i++)
            {
                TextKit.Draw(canvas, ShellUi.Ellipsize(lines[i], width - 28, 11.5f, TextKit.Regular),
                    x, top + 40 + i * 17, 11.5f, ShellUi.Text);
            }
            if (lines.Length > shown)
            {
                TextKit.Draw(canvas, $"\u2026 +{lines.Length - shown} more lines", x, top + 40 + shown * 17,
                    10.5f, ShellUi.Fade(ShellUi.Muted, 0.9f));
            }
        }
        var count = $"{text.Length} chars";
        TextKit.Draw(canvas, count, left + width - 14 - TextKit.Measure(count, 10, TextKit.Monospace), top + 90,
            10f, ShellUi.Fade(ShellUi.Muted, 0.85f), TextKit.Monospace);
        RegisterButton(new SKRect(left + 10, top + 32, left + width - 10, top + 100), NotesEditRequested);
    }

    private void DrawWeatherWidget(SKCanvas canvas, float left, float top, float width, WinotchSettings settings)
    {
        var config = settings.Widgets.Weather;
        var x = left + 14;
        var snapshot = Weather;
        if (snapshot is null && !config.HasLocation)
        {
            TextKit.Draw(canvas, "No location set yet.", x, top + 42, 11.5f, ShellUi.Fade(ShellUi.Muted, 1));
            var chip = ChipRight(canvas, "set location", left + width - 22, top + 50, false);
            RegisterButton(chip, WeatherEditRequested);
            return;
        }
        if (snapshot is null)
        {
            TextKit.Draw(canvas, $"Loading weather for {config.City}\u2026", x, top + 46, 11.5f, ShellUi.Fade(ShellUi.Muted, 1));
            return;
        }
        TextKit.DrawCentered(canvas, snapshot.Emoji, x + 18, top + 50, 24, ShellUi.Text, TextKit.Emoji);
        TextKit.Draw(canvas, $"{Math.Round(snapshot.Temp)}", x + 44, top + 40, 26, ShellUi.Text, TextKit.Semibold);
        TextKit.Draw(canvas, "\u00B0", x + 44 + TextKit.Measure($"{Math.Round(snapshot.Temp)}", 26, TextKit.Semibold), top + 40, 26, ShellUi.Fade(ShellUi.Text, 0.8f), TextKit.Semibold);
        var meta = $"{snapshot.Label} \u00B7 feels {Math.Round(snapshot.Feels)}\u00B0 \u00B7 H {Math.Round(snapshot.Max)}\u00B0 L {Math.Round(snapshot.Min)}\u00B0";
        TextKit.Draw(canvas, ShellUi.Ellipsize(meta, width - 30, 10.5f, TextKit.Regular), x, top + 74, 10.5f, ShellUi.Fade(ShellUi.Muted, 1));
        TextKit.Draw(canvas, ShellUi.Ellipsize(snapshot.City, width - 30, 10f, TextKit.Regular), x, top + 90, 10f, ShellUi.Fade(ShellUi.Muted, 0.85f));
    }

    private void DrawLauncherWidget(SKCanvas canvas, float left, float top, float width, WinotchSettings settings)
    {
        var apps = settings.Widgets.LauncherApps.Take(12).ToList();
        if (apps.Count == 0)
        {
            TextKit.Draw(canvas, "No apps yet — add some from the \u2699 on this card.", left + 14, top + 42,
                11.5f, ShellUi.Fade(ShellUi.Muted, 0.9f));
            var chip = ChipRight(canvas, "add apps", left + width - 22, top + 50, false);
            RegisterButton(chip, LauncherEditRequested);
            return;
        }

        const int columns = 4;
        var tileWidth = (width - 28) / columns;
        const float tileHeight = 48f;
        var gridY = top + 38f;
        for (var i = 0; i < apps.Count; i++)
        {
            var col = i % columns;
            var row = i / columns;
            var centerX = left + 14 + col * tileWidth + tileWidth / 2;
            var centerY = gridY + row * 58 + tileHeight / 2;
            var hovering = Math.Abs(_cursorX - centerX) <= tileWidth / 2 - 4 && Math.Abs(_cursorY - centerY) <= tileHeight / 2;
            if (hovering)
            {
                FillRound(canvas, centerX - tileWidth / 2 + 2, centerY - tileHeight / 2, tileWidth - 4, tileHeight, 10, ShellUi.CardHover);
            }
            ShellUi.FillCircle(canvas, centerX, centerY - 12, 14, ShellUi.Fade(ShellUi.Accent, hovering ? 0.5 : 0.22));
            var name = apps[i].Label.Trim();
            var initial = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "\u2022";
            TextKit.DrawCentered(canvas, initial, centerX, centerY - 12, 12.5f,
                hovering ? ShellUi.Text : ShellUi.Fade(ShellUi.Text, 0.9f), TextKit.Semibold);
            TextKit.DrawCentered(canvas, ShellUi.Ellipsize(apps[i].Label, tileWidth - 6, 9.5f, TextKit.Regular),
                centerX, centerY + 15, 9.5f, ShellUi.Fade(ShellUi.Muted, 1));
            var app = apps[i];
            RegisterButton(new SKRect(centerX - tileWidth / 2, centerY - tileHeight / 2,
                centerX + tileWidth / 2, centerY + tileHeight / 2), () => LaunchAppRequested?.Invoke(app));
        }
    }

    private void DrawFocusWidget(SKCanvas canvas, float left, float top, float width)
    {
        var right = left + width;
        var isBreak = Focus.Phase == FocusTimerPhase.Break;
        var accent = isBreak ? ShellUi.Ok : ShellUi.Accent;
        var remaining = FocusLabel();
        if (remaining.Length > 0)
        {
            var remainingWidth = TextKit.Measure(remaining, 16, TextKit.Monospace);
            TextKit.Draw(canvas, remaining, right - 22 - remainingWidth, top + 9, 16,
                isBreak ? ShellUi.Ok : ShellUi.Text, TextKit.Monospace);
        }

        var barY = top + 34;
        var barLeft = left + 12;
        var barWidth = width - 24;
        FillRound(canvas, barLeft, barY, barWidth, 4, 2, ShellUi.Hairline);
        var progress = Focus.ProgressAt(DateTimeOffset.UtcNow);
        if (progress > 0.01)
        {
            FillRound(canvas, barLeft, barY, (float)(barWidth * progress), 4, 2, ShellUi.Fade(accent, 0.9));
        }
        DrawFocusActions(canvas, left, top, width, accent);
    }

    private void DrawClipboardWidget(SKCanvas canvas, float left, float top, float width)
    {
        var rows = Math.Clamp(ClipboardItems.Count, 1, 5);
        var right = left + width;
        var clearRect = ChipRight(canvas, "clear", right - 10, top + 16, false);
        RegisterButton(clearRect, ClearClipboardRequested);

        if (ClipboardItems.Count == 0)
        {
            TextKit.Draw(canvas, "Nothing copied yet — text, files and images you copy will appear here.",
                left + 12, top + 44, 10.5f, ShellUi.Fade(ShellUi.Muted, 0.85f));
        }
        else
        {
            var rowY = top + 34f;
            for (var i = 0; i < Math.Min(rows, ClipboardItems.Count); i++)
            {
                DrawClipboardRow(canvas, left, right, rowY, ClipboardItems[i]);
                rowY += 36;
            }
        }
    }

    // -------------------------------------------------- widget management

    private void MoveWidget(string id, int direction)
    {
        var enabled = _settings.Current.Widgets.Enabled.ToList();
        var from = enabled.IndexOf(id);
        var to = from + direction;
        if (from < 0 || to < 0 || to >= enabled.Count) return;
        (enabled[from], enabled[to]) = (enabled[to], enabled[from]);
        _settings.Update(s => s with { Widgets = s.Widgets with { Enabled = enabled } });
    }

    private void RemoveWidget(string id)
    {
        _settings.Update(s => s with
        {
            Widgets = s.Widgets with { Enabled = s.Widgets.Enabled.Where(w => w != id).ToList() },
        });
    }

    private void AddWidget(string id)
    {
        _settings.Update(s => s.Widgets.Enabled.Contains(id)
            ? s
            : s with { Widgets = s.Widgets with { Enabled = s.Widgets.Enabled.Append(id).ToList() } });
    }

    private void DrawFocusActions(SKCanvas canvas, float left, float top, float width, Col accent)
    {
        var cy = top + 88;
        var x = left + 14;
        if (Focus.IsActive)
        {
            var toggleLabel = Focus.Status == FocusTimerStatus.Paused ? "resume" : "pause";
            var rect = ChipCentered(canvas, toggleLabel, x + 38, cy,
                Focus.Status == FocusTimerStatus.Paused);
            RegisterButton(rect, Focus.Status == FocusTimerStatus.Paused ? ResumeFocus : PauseFocus);

            x = rect.Right + 10;
            var skipRect = ChipCentered(canvas, "skip", x + 28, cy, false);
            RegisterButton(skipRect, SkipFocus);

            x = skipRect.Right + 10;
            var stopRect = ChipCentered(canvas, "stop", x + 30, cy, false);
            RegisterButton(stopRect, StopFocus);
        }
        else
        {
            var p25 = ChipCentered(canvas, "25 / 5", x + 38, cy, FocusPreset25);
            RegisterButton(p25, () => StartFocus(25, 5));
            var p50 = ChipCentered(canvas, "50 / 10", p25.Right + 38, cy, FocusPreset50);
            RegisterButton(p50, () => StartFocus(50, 10));
        }
    }

    private bool FocusPreset25 => FocusPreset.FocusDuration == TimeSpan.FromMinutes(25);
    private bool FocusPreset50 => FocusPreset.FocusDuration == TimeSpan.FromMinutes(50);

    private void DrawClipboardRow(SKCanvas canvas, float left, float right, float rowY, ClipboardEntry entry)
    {
        var chipX = left + 10;
        var chipY = rowY + 6;
        if (entry.Kind == "image" && entry.ThumbBase64 is { } b64 && TryGetThumb(b64, out var thumb))
        {
            canvas.DrawBitmap(thumb, new SKRect(chipX, chipY, chipX + 24, chipY + 24));
        }
        else
        {
            FillRound(canvas, chipX, chipY, 24, 24, 6, ShellUi.CardHover);
            var letter = entry.Kind == "text" ? "T" : "F";
            TextKit.DrawCentered(canvas, letter, chipX + 12, chipY + 12, 11, ShellUi.Fade(ShellUi.Muted, 1), TextKit.Semibold);
        }

        var textX = chipX + 34;
        var copyX = right - 36;
        var delX = right - 9;
        var text = ShellUi.Ellipsize(entry.Preview(60), copyX - textX - 8, 11f, TextKit.Regular);
        TextKit.Draw(canvas, text, textX, chipY + 6, 11f, ShellUi.Text);

        ShellUi.DrawGlyph(canvas, "\uE8C8", copyX, chipY + 12, 11.5f, ShellUi.Muted);
        ShellUi.DrawGlyph(canvas, "\uE74D", delX, chipY + 12, 11.5f, ShellUi.Muted);
        RegisterButton(new SKRect(copyX - 11, chipY - 2, copyX + 11, chipY + 26),
            () => CopyClipboardEntryRequested?.Invoke(entry.Id));
        RegisterButton(new SKRect(delX - 11, chipY - 2, delX + 11, chipY + 26),
            () => DeleteClipboardEntryRequested?.Invoke(entry.Id));
    }

    private void DrawCard(SKCanvas canvas, float x, float y, float w, float h)
    {
        FillRound(canvas, x, y, w, h, 13, ShellUi.Card);
    }

    private void DrawIconButton(SKCanvas canvas, float cx, float cy, string glyph, Action? action)
    {
        var hovering = Math.Abs(_cursorX - cx) <= 16 && Math.Abs(_cursorY - cy) <= 16;
        if (hovering)
        {
            ShellUi.FillCircle(canvas, cx, cy, 15, ShellUi.CardHover);
        }
        ShellUi.DrawGlyph(canvas, glyph, cx, cy, 12.5f, hovering ? ShellUi.Text : ShellUi.Fade(ShellUi.Text, 0.78f));
        if (action is not null)
        {
            RegisterButton(new SKRect(cx - 15, cy - 15, cx + 15, cy + 15), action);
        }
    }

    private SKRect ChipCentered(SKCanvas canvas, string text, float centerX, float cy, bool selected)
    {
        var w = TextKit.Measure(text, 11f, TextKit.Semibold) + 24;
        var left = centerX - w / 2;
        DrawChip(canvas, text, left, cy, w, selected);
        return new SKRect(left, cy - 14, left + w, cy + 14);
    }

    private SKRect ChipRight(SKCanvas canvas, string text, float right, float cy, bool selected)
    {
        var w = TextKit.Measure(text, 11f, TextKit.Semibold) + 24;
        var left = right - w;
        DrawChip(canvas, text, left, cy, w, selected);
        return new SKRect(left, cy - 14, right, cy + 14);
    }

    private void DrawChip(SKCanvas canvas, string text, float left, float cy, float w, bool selected)
    {
        var bg = selected ? ShellUi.Accent : ShellUi.CardHover;
        var fg = selected ? Col.Rgba(0x06, 0x13, 0x1A) : ShellUi.Text;
        FillRound(canvas, left, cy - 14, w, 28, 14, bg);
        TextKit.DrawCentered(canvas, text, left + w / 2, cy, 11f, fg, TextKit.Semibold);
    }

    private void DrawTimeline(SKCanvas canvas, float x, float y, float width, MediaSnapshot media)
    {
        FillRound(canvas, x, y, width, 2, 1, ShellUi.Hairline);
        if (media.DurationSeconds <= 0) return;
        var ratio = (float)Math.Clamp(media.PositionSeconds / media.DurationSeconds, 0, 1);
        if (ratio > 0.01)
        {
            FillRound(canvas, x, y, width * ratio, 2, 1, ShellUi.Fade(ShellUi.Accent, 0.85));
        }
    }

    private void DrawToasts(SKCanvas canvas)
    {
        if (_toasts.Count == 0) return;
        var (tx, _, tw) = ToastAnchor();
        var y = (float)(_y + _h) + 14;
        foreach (var toast in _toasts.Take(3))
        {
            var opacity = toast.Opacity;
            if (opacity <= 0.02) continue;
            var rise = (float)((1 - opacity) * 10);
            var alpha = Math.Min(1.0, opacity * 1.35);
            var cardH = toast.Spec.Flourish ? 62f : 54f;

            FillRound(canvas, tx, y - rise, tw, cardH, 16, ShellUi.Fade(Col.Rgba(0x13, 0x15, 0x1B), alpha));
            ShellUi.StrokeRound(canvas, tx, y - rise, tw, cardH, 16, 1f, ShellUi.Fade(ShellUi.Stroke, alpha * 0.5));

            var iconX = tx + 27;
            var iconY = y - rise + cardH / 2;
            var hasIcon = toast.Spec.Icon.Length > 0;
            if (hasIcon)
            {
                TextKit.DrawCentered(canvas, toast.Spec.Icon, iconX, iconY, 19, ShellUi.Fade(Col.White, alpha), TextKit.Emoji);
            }
            var textX = hasIcon ? iconX + 15 : tx + 20;
            var textW = tw - (textX - tx) - 10;
            TextKit.Draw(canvas, ShellUi.Ellipsize(toast.Spec.Title, textW, 12.5f, TextKit.Semibold),
                textX, y - rise + 11, 12.5f, ShellUi.Fade(ShellUi.Text, alpha), TextKit.Semibold);
            TextKit.Draw(canvas, ShellUi.Ellipsize(toast.Spec.Body, textW, 10f, TextKit.Regular),
                textX, y - rise + 31, 10f, ShellUi.Fade(ShellUi.Muted, alpha));

            if (toast.Spec.Flourish)
            {
                var barY = y - rise + cardH - 9;
                FillRound(canvas, tx + 18, barY, tw - 36, 3, 1.5f, ShellUi.Hairline);
                var fill = toast.FlourishFill;
                if (fill > 0.01)
                {
                    FillRound(canvas, tx + 18, barY, (float)((tw - 36) * fill), 3, 1.5f, ShellUi.Ok);
                }
            }
            y += cardH + 9;
        }
    }

    private (float X, float Y, float W) ToastAnchor()
    {
        var w = 300f;
        var x = (float)(_x + (_w - w) / 2);
        return (x, (float)(_y + _h), w);
    }

    // ---------------------------------------------------------- text helpers

    private string FocusLabel()
    {
        if (!Focus.IsActive) return "";
        var remaining = Focus.RemainingAt(DateTimeOffset.UtcNow);
        var total = Math.Max(0, remaining.TotalSeconds);
        return $"{(int)(total / 60)}:{total % 60:00}";
    }

    /// <summary>Track name shown in the collapsed pill while media plays
    /// (title, else artist, else a placeholder) — the same inline now-playing
    /// the original webview app showed in the pill.</summary>
    private string MiniTrackLabel()
    {
        if (Media is not { } media) return "";
        if (media.Title.Length > 0) return media.Title;
        if (media.Artist.Length > 0) return media.Artist;
        return "Now playing";
    }

    private string ClockText(WinotchSettings settings) =>
        DateTime.Now.ToString(settings.General.Use24HourClock ? "H:mm" : "h:mm");

    private string DateText(WinotchSettings settings) =>
        settings.General.ShowDate ? DateTime.Now.ToString("ddd, MMM d") : "";

    private string BatteryText()
    {
        if (Battery.Percent >= 100 && !Battery.IsCharging) return "";
        return $"{Battery.Percent}%";
    }

    private Col LedColor()
    {
        if (Battery.IsCharging) return ShellUi.Ok;
        if (Battery.Percent <= 20) return ShellUi.Danger;
        return ShellUi.Accent;
    }

    private Vec2 CursorPoint => new(_cursorX, _cursorY);

    private void RegisterButton(SKRect bounds, Action? action)
    {
        if (action is not null) _buttons.Add(new HitButton(bounds, action));
    }

    private static void FillRound(SKCanvas canvas, float x, float y, float w, float h, float r, Col color) =>
        ShellUi.FillRound(canvas, x, y, w, h, r, color);

    // --------------------------------------------------------- focus actions

    private void StartFocus(int focusMin, int breakMin)
    {
        var preset = new FocusTimerSettings(
            TimeSpan.FromMinutes(focusMin), TimeSpan.FromMinutes(breakMin), FocusPreset.AutoCycle);
        FocusPreset = preset;
        Focus = FocusTimerState.Start(preset, DateTimeOffset.UtcNow);
    }

    private void PauseFocus()
    {
        if (Focus.Status == FocusTimerStatus.Running) Focus = Focus.Pause(DateTimeOffset.UtcNow);
    }

    private void ResumeFocus()
    {
        if (Focus.Status == FocusTimerStatus.Paused) Focus = Focus.Resume(DateTimeOffset.UtcNow);
    }

    private void SkipFocus()
    {
        if (Focus.IsActive) Focus = Focus.Skip(DateTimeOffset.UtcNow, OnPhaseComplete);
    }

    private void StopFocus()
    {
        if (Focus.IsActive) Focus = Focus.Stop();
    }

    // ------------------------------------------------------------- clipboard

    private bool TryGetThumb(string base64, out SKBitmap bitmap)
    {
        if (_thumbs.TryGetValue(base64, out bitmap!)) return true;
        try
        {
            using var data = SKData.CreateCopy(Convert.FromBase64String(base64));
            using var decoded = SKBitmap.Decode(data);
            if (decoded is null) return false;
            bitmap = decoded.Resize(new SKImageInfo(24, 24), SKFilterQuality.Medium);
            if (bitmap is null) return false;
            _thumbs[base64] = bitmap;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
