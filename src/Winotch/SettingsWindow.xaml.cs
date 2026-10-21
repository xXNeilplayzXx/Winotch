using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Winotch.Engine;

namespace Winotch;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly Action? _rerunSetup;
    private readonly Action? _factoryReset;
    private readonly Action? _enterDragMode;
    private readonly Action? _checkForUpdates;
    private readonly Func<BrightnessSource>? _brightnessSource;
    private readonly string? _monitorDeviceName;
    private readonly Action? _requestWindowsNotificationAccess;

    /// <summary>The subtitle of the brightness row, kept so the live "which
    /// interface is it reading" line can be refreshed after the row is built.</summary>
    private TextBlock? _brightnessSub;

    /// <summary>Ids of the sections the user has open. Loaded from the settings
    /// and written back on every toggle, so closing and reopening the window
    /// doesn't fold away what the user had unfolded.</summary>
    private readonly HashSet<string> _openSections;

    /// <summary>True while a WPF slider owns the mouse for an active drag. A
    /// settings refresh must not replace the slider until the drag completes.</summary>
    public bool IsDraggingSlider => Mouse.Captured is DependencyObject captured && IsSliderInAncestry(captured);

    private static bool IsSliderInAncestry(DependencyObject current)
    {
        for (DependencyObject? node = current; node is not null; node = node is Visual ? VisualTreeHelper.GetParent(node) : null)
        {
            if (node is Slider) return true;
        }
        return false;
    }

    public SettingsWindow(SettingsService settings, StartupService startup,
        Action? rerunSetup = null, Action? factoryReset = null, Action? enterDragMode = null,
        Action? checkForUpdates = null, Func<BrightnessSource>? brightnessSource = null,
        string? monitorDeviceName = null, Action? requestWindowsNotificationAccess = null)
    {
        _settings = settings;
        _startup = startup;
        _rerunSetup = rerunSetup;
        _factoryReset = factoryReset;
        _enterDragMode = enterDragMode;
        _checkForUpdates = checkForUpdates;
        _brightnessSource = brightnessSource;
        _monitorDeviceName = monitorDeviceName;
        _requestWindowsNotificationAccess = requestWindowsNotificationAccess;
        _lastMonitorProfiles = settings.Current.MonitorProfiles;
        _lastProfilesEnabled = settings.Current.Appearance.UseMonitorProfiles;
        _openSections = new HashSet<string>(
            settings.Current.General.OpenSettingsSections
                .Select(GeneralSettings.NormalizeSectionId)
                .Where(id => id is not null)
                .Select(id => id!), StringComparer.OrdinalIgnoreCase);
        InitializeComponent();
        VersionText.Text = AssemblyVersion();
        BuildSections();
        BuildFooterActions();
        _settings.Changed += OnSettingsChanged;
        Closed += (_, _) => _settings.Changed -= OnSettingsChanged;
    }

    /// <summary>Sticky footer actions: reset everything to defaults, or wipe
    /// and restart fresh (item: applying / resetting / factory reset).</summary>
    private void BuildFooterActions()
    {
        var reset = DialogUi.Button("Reset to defaults");
        reset.Margin = new Thickness(0, 0, 8, 0);
        reset.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "Reset every Winotch setting back to its default?",
                    "Reset to defaults", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            {
                return;
            }
            UpdateSettings(_ => WinotchSettings.Defaults);
            _openSections.Clear();
            Rows.Children.Clear();
            BuildSections();
        };
        var factory = DialogUi.Button("Factory reset…", danger: true);
        factory.Click += (_, _) =>
        {
            if (MessageBox.Show(this,
                    "Factory reset wipes ALL settings (notes, apps, fonts, layout) and restarts Winotch fresh. Continue?",
                    "Factory reset", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }
            _factoryReset?.Invoke();
        };
        FooterActions.Children.Add(reset);
        FooterActions.Children.Add(factory);
    }

    private void OnSettingsChanged(object? sender, WinotchSettings settings)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_settingsWindowMonitorProfileChanged(settings))
            {
                _lastMonitorProfiles = settings.MonitorProfiles;
                _lastProfilesEnabled = settings.Appearance.UseMonitorProfiles;
                Rows.Children.Clear();
                BuildSections();
            }
        });
    }

    private bool _settingsWindowMonitorProfileChanged(WinotchSettings settings) =>
        !ReferenceEquals(settings.MonitorProfiles, _lastMonitorProfiles) ||
        settings.Appearance.UseMonitorProfiles != _lastProfilesEnabled;

    private IReadOnlyDictionary<string, MonitorProfile> _lastMonitorProfiles = new Dictionary<string, MonitorProfile>();
    private bool _lastProfilesEnabled;

    private string AssemblyVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "" : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>Re-skin every WPF surface from the resolved shell theme
    /// (called live by the overlay when the theme/accent changes). When the
    /// theme actually changed (<paramref name="rebuild"/>), the rows are
    /// rebuilt so their brushes follow the new palette — a live light/dark
    /// switch never leaves stale colors behind.</summary>
    public void ApplyTheme(ShellTheme theme, bool rebuild = false)
    {
        DialogUi.Current = theme;
        if (Application.Current.Resources["NotchUiFont"] is FontFamily uiFont)
        {
            FontFamily = uiFont;
        }
        // If no icon face could be found, every glyph in this window is blank.
        // Say so where the user is already looking, rather than leaving a window
        // full of unexplained gaps (see IconFont / IconFontWpf).
        if (IconFontWpf.Note() is { } iconNote)
        {
            IconHint.Text = iconNote;
            IconHint.Visibility = Visibility.Visible;
        }
        // The brightness row's subtitle names the interface being read, which can
        // change while this window is open (a monitor plugged in, the poll
        // answering late).
        if (_brightnessSub is not null)
        {
            _brightnessSub.Text = BrightnessSubtitle(ScopedSettings.Features.BrightnessHudEnabled);
        }
        SetBrush("NotchPanel", theme.WpfPanelTop);
        SetBrush("NotchPanelRaised", theme.WpfCard);
        SetBrush("NotchPanelHover", theme.WpfCardHover);
        SetBrush("NotchStroke", theme.WpfStroke);
        SetBrush("NotchAccent", theme.WpfAccent);
        SetBrush("NotchText", theme.WpfText);
        SetBrush("NotchMutedText", theme.WpfMuted);
        SetBrush("NotchHairline", theme.WpfHairline);
        SetBrush("NotchOnAccent", theme.WpfOnAccent);
        SetBrush("NotchOk", theme.WpfOk);
        SetBrush("NotchDanger", theme.WpfDanger);
        if (rebuild)
        {
            Rows.Children.Clear();
            BuildSections();
        }
    }

    private static void SetBrush(string key, Color color) =>
        Application.Current.Resources[key] = new SolidColorBrush(color);

    private Border _opacityRow = null!;
    private Border _blurRow = null!;
    private Border _blurEnabledRow = null!;
    private Border _customIdleRow = null!;

    private WinotchSettings ScopedSettings => string.IsNullOrWhiteSpace(_monitorDeviceName)
        ? _settings.Current
        : MonitorProfileStore.Resolve(_settings.Current, _monitorDeviceName);

    private void UpdateSettings(Func<WinotchSettings, WinotchSettings> change)
    {
        if (string.IsNullOrWhiteSpace(_monitorDeviceName))
        {
            _settings.Update(change);
            return;
        }
        _settings.Update(current =>
        {
            var updated = Scoped(current, _monitorDeviceName, change);
            return current.Appearance.UseMonitorProfiles
                ? MonitorProfileStore.Save(current, _monitorDeviceName, updated)
                : updated;
        });
    }

    private static WinotchSettings Scoped(WinotchSettings current, string deviceName,
        Func<WinotchSettings, WinotchSettings> change)
    {
        var effective = MonitorProfileStore.Resolve(current, deviceName);
        var updated = change(effective);
        return updated with
        {
            Appearance = updated.Appearance with
            {
                UseMonitorProfiles = current.Appearance.UseMonitorProfiles,
                Display = current.Appearance.Display,
                DisplayDeviceName = current.Appearance.DisplayDeviceName,
            },
        };
    }


    private void BuildSections()
    {
        var s = ScopedSettings;

        _opacityRow = MakeSliderRow("Opacity", "Glass translucency (10–100%)",
            10, 100, s.Appearance.Opacity,
            v => UpdateAppearance(x => x with { Opacity = (int)v }));
        _blurRow = MakeSliderRow("Blur", "Backdrop blur strength (0–40px)",
            0, 40, s.Appearance.Blur,
            v => UpdateAppearance(x => x with { Blur = (int)v }));
        _blurEnabledRow = MakeToggleRow("Backdrop blur",
            "Blur what's behind the island (off = plain translucent tint)",
            s.Appearance.BlurEnabled,
            v =>
            {
                UpdateAppearance(x => x with { BlurEnabled = v });
                // The strength slider matters only while blur is on.
                _blurRow.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
            });
        _blurEnabledRow.Visibility = s.Appearance.Glass ? Visibility.Visible : Visibility.Collapsed;
        _opacityRow.Visibility = s.Appearance.Glass ? Visibility.Visible : Visibility.Collapsed;
        _blurRow.Visibility = s.Appearance.Glass && s.Appearance.BlurEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;

        var generalRows = new List<FrameworkElement>
        {
            MakeToggleRow("24-hour clock", "Show times in 24h format",
                s.General.Use24HourClock,
                v => UpdateSettings(x => x with { General = x.General with { Use24HourClock = v } })),
            MakeToggleRow("Show the date", "Day + month in the panel and clock widget",
                s.General.ShowDate,
                v => UpdateSettings(x => x with { General = x.General with { ShowDate = v } })),
            MakeToggleRow("Date on the pill", "Also show the day + month on the collapsed pill",
                s.General.ShowDateOnPill,
                v => UpdateSettings(x => x with { General = x.General with { ShowDateOnPill = v } })),
            MakeChoiceRow("Clock layout", "Stacked centers the date under the time",
                new[] { ("standard", "Standard"), ("stacked", "Stacked") },
                s.General.Layout == ClockLayout.Stacked ? "stacked" : "standard",
                v => UpdateSettings(x => x with
                {
                    General = x.General with { Layout = v == "stacked" ? ClockLayout.Stacked : ClockLayout.Standard },
                })),
        };
        if (_rerunSetup is not null)
        {
            generalRows.Add(MakeButtonRow("Rerun setup wizard", "Reopen the two-page setup", _rerunSetup));
        }
        generalRows.Add(MakeChoiceRow("Weather temperature", "Choose Auto, Celsius or Fahrenheit",
            new[] { ("auto", "Auto"), ("celsius", "°C"), ("fahrenheit", "°F") },
            s.General.WeatherTemperatureUnit.ToString().ToLowerInvariant(),
            v => UpdateSettings(x => x with { General = x.General with { WeatherTemperatureUnit = ParseWeatherTemperatureUnit(v) } })));
        AddSection("basics", "Basics", "Clock, date and weather preferences", generalRows.ToArray());

        AddSection("appearance", "Appearance", "Theme, accent, font, surface, glass and layout",
            MakeChoiceRow("Theme", "System follows Windows; Colour tints the island; Background follows your wallpaper",
                new[]
                {
                    ("dark", "Dark"), ("light", "Light"), ("system", "System"),
                    ("color", "Colour"), ("background", "Background"),
                }, s.Appearance.Theme.ToString().ToLowerInvariant(),
                v => UpdateAppearance(x => x with { Theme = ParseTheme(v) })),
            MakeAccentRow("Accent", "The highlight color — preset, custom, or Windows",
                s.Appearance),
            MakeFontRow(s.Appearance),
            MakeChoiceRow("Gradient", "A background skin over the island body",
                GradientSkin.Known.Select(g => (g, GradientSkin.Labels[g])).ToArray(),
                s.Appearance.Gradient,
                v => UpdateAppearance(x => x with { Gradient = v })),
            MakeChoiceRow("Surface", "Glass is translucent; Acrylic is heavier blur with an accent frost; Solid is opaque",
                new[]
                {
                    ("glass", "Glass"), ("acrylic", "Acrylic"), ("solid", "Solid"),
                }, SurfaceKey(s.Appearance.Surface),
                v =>
                {
                    var surface = ParseSurface(v);
                    var glass = surface != SurfaceMode.Solid;
                    UpdateAppearance(x => x with { Surface = surface, Glass = glass });
                    var blurEnabled = ScopedSettings.Appearance.BlurEnabled;
                    _blurEnabledRow.Visibility = glass ? Visibility.Visible : Visibility.Collapsed;
                    _opacityRow.Visibility = glass ? Visibility.Visible : Visibility.Collapsed;
                    _blurRow.Visibility = glass && blurEnabled ? Visibility.Visible : Visibility.Collapsed;
                }),
            MakeChoiceRow("Layout",
                "Classic and Glance are the same notch at the same size; Glance repaints it in the high-gloss glass skin",
                new[] { ("classic", "Classic"), ("glance", "Glance") },
                s.Appearance.ShellLayout == ShellLayoutMode.Glance ? "glance" : "classic",
                v =>
                {
                    var layout = ParseShellLayout(v);
                    // Glance implies the glossy skin. Keep the separate
                    // Classic preference intact so returning to Classic restores it.
                    UpdateAppearance(x => x.WithShellLayout(layout));
                }),
            MakeToggleRow("Glossy glass",
                "The milled midnight capsule with its blue glow and halo, and framed slate cards (also implied by Glance)",
                s.Appearance.GlossyGlass,
                v => UpdateAppearance(x => x with { GlossyGlass = v })),
            _blurEnabledRow,
            _opacityRow,
            _blurRow);

        var profileRows = new List<FrameworkElement>
        {
            MakeToggleRow("Profiles for each monitor", "Save and apply a complete independent settings profile per display",
                s.Appearance.UseMonitorProfiles,
                enabled =>
                {
                    _settings.Update(current => current with { Appearance = current.Appearance with { UseMonitorProfiles = enabled } });
                    if (enabled && !string.IsNullOrWhiteSpace(_monitorDeviceName) &&
                        !_settings.Current.MonitorProfiles.ContainsKey(_monitorDeviceName)) CaptureMonitorProfile();
                }),
        };
        if (!string.IsNullOrWhiteSpace(_monitorDeviceName))
        {
            profileRows.Add(MakeButtonRow("Capture settings for this monitor", "Save the current settings groups as this display's profile",
                CaptureMonitorProfile));
        }
        var displayRows = new List<FrameworkElement> { MakeDisplayControl(s.Appearance) };
        displayRows.AddRange(profileRows);
        displayRows.Add(MakeToggleRow("Start with Windows", "Automatically launch when you sign in", s.General.StartWithWindows,
            v =>
            {
                UpdateSettings(x => x with { General = x.General with { StartWithWindows = v } });
                _startup.SetEnabled(v, StartupService.CurrentExecutablePath());
            }));
        displayRows.Add(MakeToggleRow("Check for updates automatically", "Look for newer builds when Winotch starts", s.General.AutoCheckUpdates,
            v => UpdateSettings(x => x with { General = x.General with { AutoCheckUpdates = v } })));
        if (_checkForUpdates is not null)
            displayRows.Add(MakeButtonRow("Check for updates", "See if a newer build is available", _checkForUpdates));
        AddSection("displays", "Display & startup", "Monitor targeting, startup and per-display profiles", displayRows.ToArray());

        _customIdleRow = MakeTextBoxRow("Custom idle text", "Shown in the pill when 'Custom text' is selected",
            s.Appearance.CustomIdleText,
            text => UpdateAppearance(x => x with { CustomIdleText = text }));
        _customIdleRow.Visibility = s.Appearance.IdleContent == IdleContentMode.Custom
            ? Visibility.Visible
            : Visibility.Collapsed;
        var defaultMiniWidgetRow = MakeChoiceRow("Default mini widget",
            "Choose the live readout shown beside the clock when Idle content is Default",
            new[]
            {
                ("battery", "Battery"), ("weather", "Weather"), ("cpu", "CPU"),
                ("memory", "Memory"), ("none", "None"),
            }, MiniWidgetKey(s.Appearance.ResolveDefaultMiniWidget(s.Features.ShowBatteryOnPill)),
            v => UpdateAppearance(x => x with { DefaultMiniWidget = ParseMiniWidget(v) }));
        defaultMiniWidgetRow.Visibility = s.Appearance.IdleContent == IdleContentMode.Auto
            ? Visibility.Visible
            : Visibility.Collapsed;

        var pillRows = new List<FrameworkElement>
        {
            MakeChoiceRow("Idle content", "What the collapsed pill shows when nothing is playing",
                new[]
                {
                    ("auto", "Default"), ("brandclock", "Brand + clock"),
                    ("custom", "Custom text"), ("dotonly", "Dot only"),
                }, IdleKey(s.Appearance.IdleContent),
                v =>
                {
                    UpdateAppearance(x => x with { IdleContent = ParseIdle(v) });
                    _customIdleRow.Visibility = v == "custom" ? Visibility.Visible : Visibility.Collapsed;
                    defaultMiniWidgetRow.Visibility = v == "auto" ? Visibility.Visible : Visibility.Collapsed;
                }),
            defaultMiniWidgetRow,
            _customIdleRow,
            MakeChoiceRow("Edge attachment", "Top = the usual pill; Left / Right rotate it vertically onto that edge",
                new[] { ("top", "Top"), ("left", "Left"), ("right", "Right") },
                EdgeKey(s.General.Edge),
                v =>
                {
                    var edge = ParseEdge(v);
                    // Switching edges re-anchors the pill; drop the old drag
                    // offset so it doesn't land in a stale spot on the new edge.
                    var appearance = edge == ScopedSettings.General.Edge
                        ? ScopedSettings.Appearance
                        : ScopedSettings.Appearance with { OffsetX = 0, OffsetY = 0 };
                    UpdateSettings(x => x with
                    {
                        General = x.General with { Edge = edge },
                        Appearance = appearance,
                    });
                }),
            MakeToggleRow("Vertical text", "Side mode: keep letters upright, stacked top-to-bottom",
                s.Appearance.VerticalText,
                v => UpdateAppearance(x => x with { VerticalText = v })),
            MakeChoiceRow("Open direction", "How the panel expands: one column downward, or wider with widgets in two columns",
                new[] { ("vertical", "Vertical"), ("horizontal", "Horizontal") },
                s.General.OpenDirection == ExpandDirection.Horizontal ? "horizontal" : "vertical",
                v => UpdateSettings(x => x with
                {
                    General = x.General with
                    {
                        OpenDirection = v == "horizontal" ? ExpandDirection.Horizontal : ExpandDirection.Vertical,
                    },
                })),
        };
        if (_enterDragMode is not null)
        {
            pillRows.Add(MakeButtonRow("Drag mode",
                "Show the pill with Done / Reset buttons so you can reposition it", _enterDragMode));
        }
        pillRows.Add(MakeToggleRow("Volume keys", "Take the volume keys over so the pill shows the level instead of Windows' OSD",
            s.Features.VolumeHudEnabled,
            v => UpdateSettings(x => x with { Features = x.Features with { VolumeHudEnabled = v } })));
        pillRows.Add(MakeToggleRow("Brightness on the pill", BrightnessSubtitle(s.Features.BrightnessHudEnabled),
            s.Features.BrightnessHudEnabled,
            v => UpdateSettings(x => x with { Features = x.Features with { BrightnessHudEnabled = v } }),
            sub => _brightnessSub = sub));
        pillRows.Add(MakeToggleRow("Media key feedback", "Show the track on the pill when you press play / pause / next",
            s.Features.MediaHudEnabled,
            v => UpdateSettings(x => x with { Features = x.Features with { MediaHudEnabled = v } })));
        pillRows.Insert(1, MakeSizeControl(s.Appearance));
        pillRows.Insert(0, MakeChoiceRow("Position", "Notch tucks into the top edge; Island floats below it",
            new[] { ("notch", "Notch"), ("island", "Island") },
            s.General.Style == NotchStyle.Notch ? "notch" : "island",
            v => UpdateSettings(x => x with
            {
                General = x.General with { Style = v == "notch" ? NotchStyle.Notch : NotchStyle.Island },
                Appearance = x.General.Style == (v == "notch" ? NotchStyle.Notch : NotchStyle.Island)
                    ? x.Appearance : x.Appearance with { OffsetY = 0 },
            })));
        AddSection("pill", "Pill", "Position, size, idle content and controls", pillRows.ToArray());

        AddSection("calls", "Calls & media", "What appears on the pill during calls, playback and focus",
            MakeToggleRow("Calls on the pill", "Show a green call chip while you're on a call",
                s.Features.ShowCallsOnPill,
                v => UpdateSettings(x => x with { Features = x.Features with { ShowCallsOnPill = v } })),
            MakeChoiceRow("Call chip content", "Put the timer first when a long app name needs room",
                new[] { ("app", "App name + timer"), ("timer-first", "Timer + app name"), ("app-only", "App name only"), ("timer-only", "Timer only") },
                CallChipContentKey(s.Features.CallChipPriority),
                v => UpdateSettings(x => x with { Features = x.Features with { CallChipPriority = ParseCallChipContent(v) } })),
            MakeToggleRow("Now playing on the pill", "Show a track chip while music plays",
                s.Features.ShowMediaOnPill,
                v => UpdateSettings(x => x with { Features = x.Features with { ShowMediaOnPill = v } })),
            MakeToggleRow("Media key feedback", "Show feedback when media keys are pressed",
                s.Features.MediaHudEnabled,
                v => UpdateSettings(x => x with { Features = x.Features with { MediaHudEnabled = v } })),
            MakeToggleRow("Focus timer on the pill", "Show the active timer on the pill",
                s.Features.ShowFocusOnPill,
                v => UpdateSettings(x => x with { Features = x.Features with { ShowFocusOnPill = v } })));

        AddSection("notifications", "Alerts & quiet mode", "Toasts, rain, priority alerts, quiet modes and notification access",
            MakeToggleRow("Now-playing toasts", "Pop a toast when the track changes",
                s.Toasts.MediaToastsEnabled,
                v => UpdateSettings(x => x with { Toasts = x.Toasts with { MediaToastsEnabled = v } })),
            MakeToggleRow("Priority alerts", "Low battery, charger, Wi-Fi, mic / camera events",
                s.Features.PriorityAlertsEnabled,
                v => UpdateSettings(x => x with { Features = x.Features with { PriorityAlertsEnabled = v } })),
            MakeToggleRow("Rain alerts", "Show a heads-up when precipitation is expected soon",
                s.Toasts.RainAlertsEnabled,
                v => UpdateSettings(x => x with { Toasts = x.Toasts with { RainAlertsEnabled = v } })),
            MakeToggleRow("Quiet during focus", "Suppress media and rain feedback during a focus session", s.Features.QuietDuringFocus,
                v => UpdateSettings(x => x with { Features = x.Features with { QuietDuringFocus = v } })),
            MakeToggleRow("Quiet mode", "Temporarily suppress pill toasts and feedback", s.Widgets.QuietMode,
                v => UpdateSettings(x => x with { Widgets = x.Widgets with { QuietMode = v } })),
            MakeButtonRow("Windows notification history", "Request Windows Notification Center access (MSIX only)",
                () => _requestWindowsNotificationAccess?.Invoke()));

        AddSection("widgets", "Widgets", "Expanded-panel cards and their features",
            MakeButtonRow("Configure quick actions", "Choose which buttons appear in the Quick actions card", OpenQuickActionsEditor),
            MakeButtonRow("Calendar source", "Choose Windows calendars or import a local .ics file", OpenCalendarEditor),
            MakeToggleRow("Clipboard history", "In-memory last 10 items — never written to disk",
                s.Features.ClipboardHistoryEnabled,
                v => UpdateSettings(x => x with { Features = x.Features with { ClipboardHistoryEnabled = v } })),
            // Widget card sizes moved into each card's own ⚙ settings dialog,
            // so the card layout is per-widget instead of one size for all.
            MakeToggleRow("Focus timer", "Sessions with breaks — pick a preset or a custom ratio",
                s.Features.FocusTimerEnabled,
                v => UpdateSettings(x => x with { Features = x.Features with { FocusTimerEnabled = v } })));
    }

    private void UpdateAppearance(Func<AppearanceSettings, AppearanceSettings> change) =>
        UpdateSettings(x => x with { Appearance = change(x.Appearance) });

    private static WeatherTemperatureUnit ParseWeatherTemperatureUnit(string value) => value switch
    {
        "celsius" => WeatherTemperatureUnit.Celsius,
        "fahrenheit" => WeatherTemperatureUnit.Fahrenheit,
        _ => WeatherTemperatureUnit.Auto,
    };

    private static string CallChipContentKey(CallChipContent content) => content switch
    {
        CallChipContent.TimerAndApp => "timer-first",
        CallChipContent.AppName => "app-only",
        CallChipContent.Timer => "timer-only",
        _ => "app",
    };

    private static CallChipContent ParseCallChipContent(string value) => value switch
    {
        "timer-first" => CallChipContent.TimerAndApp,
        "app-only" => CallChipContent.AppName,
        "timer-only" => CallChipContent.Timer,
        _ => CallChipContent.AppAndTimer,
    };

    private static string EdgeKey(PillEdge edge) => edge switch
    {
        PillEdge.Left => "left",
        PillEdge.Right => "right",
        _ => "top",
    };

    private static PillEdge ParseEdge(string value) => value switch
    {
        "left" => PillEdge.Left,
        "right" => PillEdge.Right,
        _ => PillEdge.Top,
    };

    private static string MiniWidgetKey(IdleMiniWidget widget) => widget switch
    {
        IdleMiniWidget.Weather => "weather",
        IdleMiniWidget.Cpu => "cpu",
        IdleMiniWidget.Memory => "memory",
        IdleMiniWidget.None => "none",
        _ => "battery",
    };

    private static IdleMiniWidget ParseMiniWidget(string value) => value switch
    {
        "weather" => IdleMiniWidget.Weather,
        "cpu" => IdleMiniWidget.Cpu,
        "memory" => IdleMiniWidget.Memory,
        "none" => IdleMiniWidget.None,
        _ => IdleMiniWidget.Battery,
    };

    private static string IdleKey(IdleContentMode mode) => mode switch
    {
        IdleContentMode.BrandClock => "brandclock",
        IdleContentMode.ClockOnly => "clockonly",
        IdleContentMode.Custom => "custom",
        IdleContentMode.DotOnly => "dotonly",
        _ => "auto",
    };

    private static IdleContentMode ParseIdle(string value) => value switch
    {
        "brandclock" => IdleContentMode.BrandClock,
        "clockonly" => IdleContentMode.ClockOnly,
        "custom" => IdleContentMode.Custom,
        "dotonly" => IdleContentMode.DotOnly,
        _ => IdleContentMode.Auto,
    };

    private static ThemeMode ParseTheme(string value) => value switch
    {
        "light" => ThemeMode.Light,
        "system" => ThemeMode.System,
        "color" => ThemeMode.Color,
        "background" => ThemeMode.Background,
        _ => ThemeMode.Dark,
    };

    private static string SurfaceKey(SurfaceMode mode) => mode switch
    {
        SurfaceMode.Acrylic => "acrylic",
        SurfaceMode.Solid => "solid",
        _ => "glass",
    };

    private static SurfaceMode ParseSurface(string value) => value switch
    {
        "acrylic" => SurfaceMode.Acrylic,
        "solid" => SurfaceMode.Solid,
        _ => SurfaceMode.Glass,
    };

    private static ShellLayoutMode ParseShellLayout(string value) =>
        value == "glance" ? ShellLayoutMode.Glance : ShellLayoutMode.Classic;

    // ------------------------------------------------------------- sections

    /// <summary>One collapsible section: a header that always shows, and the
    /// rows folded away behind it. Sections ship folded, so the window reads as
    /// a short list of headings rather than one long scroll; whichever ones are
    /// open is persisted, so a reopened window looks the way it was left.</summary>
    private void AddSection(string id, string title, string summary, params FrameworkElement[] rows)
    {
        var content = new StackPanel { Margin = new Thickness(4, 0, 4, 4) };
        for (var i = 0; i < rows.Length; i++)
        {
            content.Children.Add(rows[i]);
            if (i < rows.Length - 1)
            {
                content.Children.Add(new Border
                {
                    Height = 1,
                    Background = (Brush)FindResource("NotchHairline"),
                    Margin = new Thickness(10, 0, 10, 0),
                });
            }
        }

        // The header carries the label brighter than the old static caption —
        // it is a control now — and a one-line hint that only shows while the
        // section is folded (once the rows are visible, the hint is noise).
        var heading = new TextBlock
        {
            Text = title.ToUpperInvariant(),
            Style = (Style)FindResource("SectionLabelText"),
            Foreground = (Brush)FindResource("NotchText"),
        };
        var subtitle = new TextBlock
        {
            Text = summary,
            FontSize = 10.5,
            Foreground = (Brush)FindResource("NotchMutedText"),
            Margin = new Thickness(0, 3, 0, 0),
        };
        var chevron = new TextBlock
        {
            FontFamily = TryFindResource(IconFontWpf.ResourceKey) as FontFamily
                ?? new FontFamily(IconFontWpf.FallbackFamilies),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 2, 0),
        };

        var labels = new StackPanel();
        labels.Children.Add(heading);
        labels.Children.Add(subtitle);

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(labels, 0);
        Grid.SetColumn(chevron, 1);
        headerRow.Children.Add(labels);
        headerRow.Children.Add(chevron);

        var open = _openSections.Contains(id);
        var header = new ToggleButton
        {
            Style = (Style)FindResource("SectionHeaderToggle"),
            IsChecked = open,
            Content = headerRow,
        };

        void ApplyState(bool isOpen)
        {
            content.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
            subtitle.Visibility = isOpen ? Visibility.Collapsed : Visibility.Visible;
            chevron.Text = isOpen ? "\uE70E" : "\uE70D";
            chevron.Foreground = (Brush)FindResource(isOpen ? "NotchAccent" : "NotchMutedText");
        }

        // Applied before the handlers are wired, so building the window never
        // reads as a user toggle (and never writes settings).
        ApplyState(open);
        header.Checked += (_, _) =>
        {
            ApplyState(true);
            RememberSection(id, open: true);
        };
        header.Unchecked += (_, _) =>
        {
            ApplyState(false);
            RememberSection(id, open: false);
        };

        var card = new Border
        {
            Background = (Brush)FindResource("NotchPanelRaised"),
            BorderBrush = (Brush)FindResource("NotchStroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 0, 0, 8),
        };
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(content);
        card.Child = stack;
        Rows.Children.Add(card);
    }

    /// <summary>Record a section's open state so it survives closing the window.</summary>
    private void RememberSection(string id, bool open)
    {
        if (open) _openSections.Add(id);
        else _openSections.Remove(id);
        UpdateSettings(s => s with
        {
            General = s.General with
            {
                OpenSettingsSections = _openSections.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            },
        });
    }

    // ------------------------------------------------------------- toggles

    /// <summary>What the brightness row says underneath its title: which
    /// interface the mirror is actually reading, or that this display doesn't
    /// report brightness at all — the difference between a bug and a machine
    /// that can't do it. Reading a field the pill's host keeps up to date, so a
    /// rebuilt row never shows a stale answer.</summary>
    private string BrightnessSubtitle(bool enabled) =>
        BrightnessMirror.Describe(_brightnessSource?.Invoke() ?? BrightnessSource.None, enabled);

    private Border MakeToggleRow(string label, string sub, bool initial, Action<bool> onChanged,
        Action<TextBlock>? captureSubtitle = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
        });
        if (!string.IsNullOrEmpty(sub))
        {
            var subtitle = new TextBlock
            {
                Text = sub,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("NotchMutedText"),
                Margin = new Thickness(0, 1, 0, 0),
            };
            captureSubtitle?.Invoke(subtitle);
            text.Children.Add(subtitle);
        }

        var toggle = new CheckBox
        {
            IsChecked = initial,
            Style = (Style)FindResource("NotchSwitch"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.Checked += (_, _) => onChanged(true);
        toggle.Unchecked += (_, _) => onChanged(false);

        Grid.SetColumn(text, 0);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(text);
        grid.Children.Add(toggle);

        return new Border
        {
            Child = grid,
            Padding = new Thickness(10, 9, 8, 9),
            Margin = new Thickness(0, 1, 0, 1),
            CornerRadius = new CornerRadius(8),
            Style = RowHoverStyle,
        };
    }

    // --------------------------------------------------------------- rows

    /// <summary>A setting with a label/sub header and a custom control below
    /// (chips, sliders, swatches).</summary>
    private Border MakeSettingRow(string label, string sub, FrameworkElement control)
    {
        var header = new StackPanel { Margin = new Thickness(10, 9, 8, 2) };
        header.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
        });
        if (!string.IsNullOrEmpty(sub))
        {
            header.Children.Add(new TextBlock
            {
                Text = sub,
                FontSize = 11,
                Foreground = (Brush)FindResource("NotchMutedText"),
                Margin = new Thickness(0, 1, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var stack = new StackPanel { Children = { header, control } };
        return new Border
        {
            Child = stack,
            Padding = new Thickness(0, 2, 0, 4),
            Margin = new Thickness(0, 1, 0, 1),
            CornerRadius = new CornerRadius(8),
            Style = RowHoverStyle,
        };
    }

    // ------------------------------------------------------------ display

    /// <summary>Display targeting: mode chips plus a visual map of the
    /// monitors (click a monitor to target it specifically).</summary>
    private void CaptureMonitorProfile()
    {
        if (string.IsNullOrWhiteSpace(_monitorDeviceName)) return;
        _settings.Update(current =>
        {
            var capture = MonitorProfileStore.Resolve(current, _monitorDeviceName);
            return MonitorProfileStore.Save(current, _monitorDeviceName, capture);
        });
    }

    private void OpenCalendarEditor()
    {
        var calendar = ScopedSettings.Widgets.Calendar;
        var useWindows = new CheckBox
        {
            Content = "Use Windows calendars (requires MSIX and permission)",
            IsChecked = calendar.UseWindowsCalendars,
            Margin = new Thickness(0, 4, 0, 10),
            Foreground = (Brush)FindResource("NotchText"),
        };
        var content = new StackPanel { Margin = new Thickness(16), Width = 330 };
        var dialog = new Window { Owner = this, Width = 360, Title = "Calendar", WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Content = content };
        DialogUi.ApplyFrame(dialog, "Calendar");
        content.Children.Add(DialogUi.Label("Calendar source", 13, weight: FontWeights.SemiBold));
        content.Children.Add(useWindows);
        content.Children.Add(DialogUi.Label("Portable installs can import a local .ics file from the calendar card.", 11, muted: true));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = DialogUi.Button("Cancel");
        var save = DialogUi.Button("Save", primary: true);
        cancel.Click += (_, _) => dialog.DialogResult = false;
        save.Click += (_, _) =>
        {
            UpdateSettings(current => current with { Widgets = current.Widgets with { Calendar = current.Widgets.Calendar with { UseWindowsCalendars = useWindows.IsChecked == true } } });
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        ((FrameworkElement)buttons.Children[1]).Margin = new Thickness(8, 0, 0, 0);
        content.Children.Add(buttons);
        dialog.ShowDialog();
    }

    private void OpenQuickActionsEditor()
    {
        var selected = ScopedSettings.Widgets.QuickActions.ToHashSet(StringComparer.Ordinal);
        var items = QuickActionCatalog.Known.Select(id => new
        {
            Id = id,
            Check = new CheckBox
            {
                Content = QuickActionCatalog.Labels[id],
                IsChecked = selected.Contains(id),
                Margin = new Thickness(2, 4, 2, 4),
                Foreground = (Brush)FindResource("NotchText"),
            },
        }).ToList();
        var save = DialogUi.Button("Save", primary: true);
        var cancel = DialogUi.Button("Cancel");
        var content = new StackPanel { Margin = new Thickness(16), Width = 260 };
        var dialog = new Window { Owner = this, Width = 300, Title = "Quick actions", WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            Content = content };
        DialogUi.ApplyFrame(dialog, "Quick actions");
        foreach (var item in items) content.Children.Add(item.Check);
        save.Click += (_, _) =>
        {
            UpdateSettings(settings => settings with { Widgets = settings.Widgets with
            {
                QuickActions = items.Where(item => item.Check.IsChecked == true).Select(item => item.Id).ToList(),
            } });
            dialog.DialogResult = true;
        };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, save } };
        ((FrameworkElement)buttons.Children[1]).Margin = new Thickness(8, 0, 0, 0);
        content.Children.Add(buttons);
        dialog.ShowDialog();
    }

    private FrameworkElement MakeDisplayControl(AppearanceSettings appearance)
    {
        var stack = new StackPanel();
        var mode = appearance.Display;
        var device = appearance.DisplayDeviceName;
        var options = new[]
        {
            (DisplayMode.Main, "Main"), (DisplayMode.All, "All monitors"),
            (DisplayMode.FollowActive, "Follow active"), (DisplayMode.Named, "Specific…"),
        };
        var chips = new List<Border>();
        var chipLabels = new List<TextBlock>();
        Border mapCard = null!;

        void Paint()
        {
            for (var i = 0; i < chips.Count; i++)
            {
                var selected = options[i].Item1 == mode;
                chips[i].Background = selected
                    ? (Brush)FindResource("NotchAccent")
                    : (Brush)FindResource("NotchPanelHover");
                chipLabels[i].Foreground = selected
                    ? (Brush)FindResource("NotchOnAccent")
                    : (Brush)FindResource("NotchText");
            }
            if (mapCard is not null)
            {
                mapCard.Visibility = mode == DisplayMode.Named ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        var wrap = new WrapPanel { Margin = new Thickness(10, 4, 8, 4) };
        foreach (var (value, labelText) in options)
        {
            var text = new TextBlock { Text = labelText, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(11, 4, 11, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Child = text,
            };
            chip.MouseLeftButtonUp += (_, _) =>
            {
                mode = value;
                if (value != DisplayMode.Named)
                {
                    device = "";
                    UpdateAppearance(x => x with { Display = value, DisplayDeviceName = "" });
                }
                else
                {
                    UpdateAppearance(x => x with { Display = value });
                }
                Paint();
            };
            chips.Add(chip);
            chipLabels.Add(text);
            wrap.Children.Add(chip);
        }
        stack.Children.Add(wrap);

        mapCard = BuildDisplayMap(device,
            picked =>
            {
                mode = DisplayMode.Named;
                device = picked;
                UpdateAppearance(x => x with { Display = DisplayMode.Named, DisplayDeviceName = picked });
                Paint();
            });
        stack.Children.Add(mapCard);
        Paint();

        return MakeSettingRow("Display", "All monitors spawns one island per screen", stack);
    }

    private static Border BuildDisplayMap(string selectedDevice, Action<string> onPicked)
    {
        var monitors = MonitorTargeting.CaptureScreens();
        var map = new Grid
        {
            Background = (Brush)Application.Current.Resources["NotchPanelHover"],
            Margin = new Thickness(10, 0, 8, 8),
            Height = 140,
        };
        var hint = new TextBlock
        {
            Text = "Click a display",
            FontSize = 11.5,
            Foreground = (Brush)Application.Current.Resources["NotchMutedText"],
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        map.Children.Add(hint);

        if (monitors.Count > 0)
        {
            var minX = monitors.Min(m => m.Bounds.Left);
            var minY = monitors.Min(m => m.Bounds.Top);
            var maxRight = monitors.Max(m => m.Bounds.Right);
            var maxBottom = monitors.Max(m => m.Bounds.Bottom);
            var scale = Math.Min(350.0 / Math.Max(1, maxRight - minX), 100.0 / Math.Max(1, maxBottom - minY));

            foreach (var monitor in monitors)
            {
                var width = Math.Max(30, monitor.Bounds.Width * scale);
                var height = Math.Max(20, monitor.Bounds.Height * scale);
                var name = monitor.IsPrimary ? "Main" : monitor.DeviceName.TrimStart('\\').Replace("DISPLAY", "Screen ");
                var button = new Border
                {
                    Width = width,
                    Height = height,
                    Background = (Brush)Application.Current.Resources["NotchPanelHover"],
                    BorderBrush = string.Equals(selectedDevice, monitor.DeviceName, StringComparison.OrdinalIgnoreCase)
                        ? (Brush)Application.Current.Resources["NotchAccent"]
                        : (Brush)Application.Current.Resources["NotchStroke"],
                    BorderThickness = new Thickness(string.Equals(selectedDevice, monitor.DeviceName, StringComparison.OrdinalIgnoreCase) ? 2 : 1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness((monitor.Bounds.Left - minX) * scale, (monitor.Bounds.Top - minY) * scale, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Cursor = Cursors.Hand,
                    Child = new TextBlock
                    {
                        Text = $"{name}\n{monitor.Bounds.Width}×{monitor.Bounds.Height}",
                        FontSize = 9.5,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = (Brush)Application.Current.Resources["NotchText"],
                    },
                };
                var deviceName = monitor.DeviceName;
                button.MouseLeftButtonUp += (_, _) => onPicked(deviceName);
                map.Children.Add(button);
            }
        }

        return new Border
        {
            Child = map,
            CornerRadius = new CornerRadius(10),
        };
    }

    // --------------------------------------------------------------- size

    /// <summary>The legacy size UI: one-click presets, width/height sliders
    /// staged as a draft, and Apply / Reset to commit or discard it.</summary>
    private FrameworkElement MakeSizeControl(AppearanceSettings appearance)
    {
        var presetId = appearance.CustomSize ? appearance.SizePreset : "";
        var draftWidth = appearance.CustomSize ? appearance.Width : SizePresets.All[1].Width;
        var draftHeight = appearance.CustomSize ? appearance.Height : SizePresets.All[1].Height;

        var stack = new StackPanel();
        var chips = new List<Border>();
        var chipLabels = new List<TextBlock>();
        var syncingSliders = false;

        var presetOptions = new[] { ("", "Auto") }
            .Concat(SizePresets.All.Select(p => (p.Id, p.Label)))
            .ToArray();

        var widthSlider = new Slider
        {
            Minimum = 150, Maximum = 800, Value = draftWidth,
            IsSnapToTickEnabled = true, TickFrequency = 5,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("NotchAccent"),
        };
        var heightSlider = new Slider
        {
            Minimum = 30, Maximum = 200, Value = draftHeight,
            IsSnapToTickEnabled = true, TickFrequency = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("NotchAccent"),
        };
        var widthValue = new TextBlock { FontSize = 11, FontFamily = new FontFamily("Consolas, Courier New"), Foreground = (Brush)FindResource("NotchMutedText"), MinWidth = 28, TextAlignment = TextAlignment.Right, Margin = new Thickness(10, 0, 2, 0) };
        var heightValue = new TextBlock { FontSize = 11, FontFamily = new FontFamily("Consolas, Courier New"), Foreground = (Brush)FindResource("NotchMutedText"), MinWidth = 28, TextAlignment = TextAlignment.Right, Margin = new Thickness(10, 0, 2, 0) };

        void PaintChips()
        {
            for (var i = 0; i < chips.Count; i++)
            {
                var selected = string.Equals(presetOptions[i].Item1, presetId, StringComparison.OrdinalIgnoreCase);
                chips[i].Background = selected
                    ? (Brush)FindResource("NotchAccent")
                    : (Brush)FindResource("NotchPanelHover");
                chipLabels[i].Foreground = selected
                    ? (Brush)FindResource("NotchOnAccent")
                    : (Brush)FindResource("NotchText");
            }
        }

        void SetSliders()
        {
            syncingSliders = true;
            widthSlider.Value = draftWidth;
            heightSlider.Value = draftHeight;
            widthValue.Text = $"{draftWidth}";
            heightValue.Text = $"{draftHeight}";
            syncingSliders = false;
        }

        void ApplyPreset(string id)
        {
            var preset = SizePresets.Find(id);
            if (preset is null)
            {
                // Auto: back to content-sized pill.
                presetId = "";
                draftWidth = SizePresets.All[1].Width;
                draftHeight = SizePresets.All[1].Height;
                UpdateAppearance(x => x with { CustomSize = false });
            }
            else
            {
                presetId = preset.Value.Id;
                draftWidth = preset.Value.Width;
                draftHeight = preset.Value.Height;
                UpdateAppearance(x => x with
                {
                    CustomSize = true,
                    Width = preset.Value.Width,
                    Height = preset.Value.Height,
                    SizePreset = preset.Value.Id,
                });
            }
            SetSliders();
            PaintChips();
        }

        var wrap = new WrapPanel { Margin = new Thickness(10, 4, 8, 4) };
        foreach (var (id, label) in presetOptions)
        {
            var text = new TextBlock { Text = label, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(11, 4, 11, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Child = text,
            };
            chip.MouseLeftButtonUp += (_, _) => ApplyPreset(id);
            chips.Add(chip);
            chipLabels.Add(text);
            wrap.Children.Add(chip);
        }
        PaintChips();
        stack.Children.Add(wrap);

        widthSlider.ValueChanged += (_, e) =>
        {
            if (syncingSliders) return;
            draftWidth = (int)e.NewValue;
            presetId = "";
            widthValue.Text = $"{draftWidth}";
            PaintChips();
        };
        heightSlider.ValueChanged += (_, e) =>
        {
            if (syncingSliders) return;
            draftHeight = (int)e.NewValue;
            presetId = "";
            heightValue.Text = $"{draftHeight}";
            PaintChips();
        };

        void CommitDraft() => UpdateAppearance(x => x with
        {
            CustomSize = true,
            Width = draftWidth,
            Height = draftHeight,
            SizePreset = SizePresets.Closest(draftWidth, draftHeight).Id,
        });

        void ResetDraft()
        {
            var committed = ScopedSettings.Appearance;
            presetId = committed.CustomSize ? committed.SizePreset : "";
            draftWidth = committed.CustomSize ? committed.Width : SizePresets.All[1].Width;
            draftHeight = committed.CustomSize ? committed.Height : SizePresets.All[1].Height;
            SetSliders();
            PaintChips();
        }

        var apply = DialogUi.Button("Apply", primary: true);
        var reset = DialogUi.Button("Reset");
        apply.Click += (_, _) => CommitDraft();
        reset.Click += (_, _) => ResetDraft();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 10, 8),
            Children = { reset, apply },
        };
        ((FrameworkElement)buttons.Children[0]).Margin = new Thickness(0, 0, 8, 0);

        stack.Children.Add(MakeSliderRow("Width", "150–800 px (staged until Apply)",
            widthSlider, widthValue));
        stack.Children.Add(MakeSliderRow("Height", "30–200 px (staged until Apply)",
            heightSlider, heightValue));
        stack.Children.Add(buttons);

        return MakeSettingRow("Size", "Presets apply instantly; sliders stage a draft (Apply / Reset)", stack);
    }

    private Border MakeSliderRow(string label, string sub, Slider slider, TextBlock valueText)
    {
        var grid = new Grid { Margin = new Thickness(10, 4, 8, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(slider, 0);
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(slider);
        grid.Children.Add(valueText);
        return MakeSettingRow(label, sub, grid);
    }

    private Border MakeButtonRow(string label, string sub, Action onClick)
    {
        var button = DialogUi.Button(label, primary: true);
        button.HorizontalAlignment = HorizontalAlignment.Right;
        button.Margin = new Thickness(0, 6, 6, 8);
        button.Click += (_, _) => onClick();
        return MakeSettingRow(label, sub, button);
    }

    private Border MakeTextBoxRow(string label, string sub, string initial, Action<string> onChanged)
    {
        var box = new TextBox
        {
            Text = initial,
            FontSize = 12,
            Margin = new Thickness(10, 4, 10, 8),
            Padding = new Thickness(8, 5, 8, 5),
            Background = (Brush)FindResource("NotchPanelHover"),
            Foreground = (Brush)FindResource("NotchText"),
            BorderBrush = (Brush)FindResource("NotchStroke"),
            BorderThickness = new Thickness(1),
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) onChanged(box.Text.Trim());
        };
        box.LostFocus += (_, _) => onChanged(box.Text.Trim());
        return MakeSettingRow(label, sub, box);
    }

    private Border MakeChoiceRow(string label, string sub, IReadOnlyList<(string Value, string Label)> options,
        string current, Action<string> onChanged)
    {
        var wrap = new WrapPanel { Margin = new Thickness(10, 4, 8, 8) };
        var chips = new List<Border>();
        var labels = new List<TextBlock>();

        void Paint()
        {
            for (var i = 0; i < chips.Count; i++)
            {
                var selected = string.Equals(options[i].Value, current, StringComparison.OrdinalIgnoreCase);
                chips[i].Background = selected
                    ? (Brush)FindResource("NotchAccent")
                    : (Brush)FindResource("NotchPanelHover");
                labels[i].Foreground = selected
                    ? (Brush)FindResource("NotchOnAccent")
                    : (Brush)FindResource("NotchText");
            }
        }

        foreach (var (value, optionLabel) in options)
        {
            var text = new TextBlock { Text = optionLabel, FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(11, 4, 11, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Child = text,
            };
            chip.MouseLeftButtonUp += (_, _) =>
            {
                current = value;
                onChanged(value);
                Paint();
            };
            chips.Add(chip);
            labels.Add(text);
            wrap.Children.Add(chip);
        }
        Paint();

        return MakeSettingRow(label, sub, wrap);
    }

    /// <summary>Font picker: live-preview tiles for the curated faces, a
    /// searchable list of every font installed on the machine, and an Import
    /// button that copies a .ttf/.otf into %LOCALAPPDATA%\Winotch\Fonts so
    /// it can be used like any other face (item: fonts from the user's
    /// computer + custom font files).</summary>
    private Border MakeFontRow(AppearanceSettings appearance)
    {
        var stack = new StackPanel();
        var wrap = new WrapPanel { Margin = new Thickness(10, 4, 8, 4) };
        var tiles = new List<Border>();
        var clockTexts = new List<TextBlock>();
        var nameTexts = new List<TextBlock>();
        var current = appearance.Font;

        void Paint()
        {
            for (var i = 0; i < tiles.Count; i++)
            {
                var selected = string.Equals(FontChoice.Known[i].Id, current, StringComparison.OrdinalIgnoreCase);
                tiles[i].Background = selected
                    ? (Brush)FindResource("NotchAccent")
                    : (Brush)FindResource("NotchPanelHover");
                clockTexts[i].Foreground = selected
                    ? (Brush)FindResource("NotchOnAccent")
                    : (Brush)FindResource("NotchText");
                nameTexts[i].Foreground = selected
                    ? (Brush)FindResource("NotchOnAccent")
                    : (Brush)FindResource("NotchMutedText");
            }
        }

        var clock = DateTime.Now.ToString(ScopedSettings.General.Use24HourClock ? "H:mm" : "h:mm");
        foreach (var (id, label) in FontChoice.Known)
        {
            var family = new FontFamily(FontChoice.FamilyOf(id));
            var clockText = new TextBlock
            {
                Text = clock,
                FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                FontFamily = family,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var nameText = new TextBlock
            {
                Text = label,
                FontSize = 9.5,
                FontFamily = family,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0),
            };
            var tile = new Border
            {
                Width = 88,
                Height = 60,
                CornerRadius = new CornerRadius(10),
                Background = (Brush)FindResource("NotchPanelHover"),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { clockText, nameText },
                },
            };
            var fontId = id;
            tile.MouseLeftButtonUp += (_, _) =>
            {
                current = fontId;
                UpdateAppearance(x => x with { Font = fontId });
                Paint();
            };
            tiles.Add(tile);
            clockTexts.Add(clockText);
            nameTexts.Add(nameText);
            wrap.Children.Add(tile);
        }
        Paint();
        stack.Children.Add(wrap);

        // Every font installed on the machine, plus imported custom fonts.
        var systemFonts = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var customFonts = (appearance.CustomFonts ?? new List<string>())
            .Where(File.Exists)
            .ToList();

        var picker = new ComboBox
        {
            Margin = new Thickness(10, 0, 8, 4),
            FontSize = 12,
            Background = (Brush)FindResource("NotchPanelHover"),
            Foreground = (Brush)FindResource("NotchText"),
            BorderBrush = (Brush)FindResource("NotchStroke"),
            Style = (Style)FindResource("NotchCombo"),
            MaxDropDownHeight = 260,
        };
        foreach (var familyName in systemFonts)
        {
            picker.Items.Add(new FontOption(FontChoice.SystemId(familyName), familyName));
        }
        foreach (var file in customFonts)
        {
            var family = FontChoice.CustomFamily(file) ?? Path.GetFileNameWithoutExtension(file);
            picker.Items.Add(new FontOption(FontChoice.CustomId(file), family + "  (imported)"));
        }
        for (var i = 0; i < picker.Items.Count; i++)
        {
            if (picker.Items[i] is FontOption option &&
                string.Equals(option.Id, current, StringComparison.OrdinalIgnoreCase))
            {
                picker.SelectedIndex = i;
                break;
            }
        }
        var applyingPicker = false;
        picker.SelectionChanged += (_, _) =>
        {
            if (applyingPicker || picker.SelectedItem is not FontOption selected) return;
            current = selected.Id;
            UpdateAppearance(x => x with { Font = selected.Id });
            Paint();
        };

        var import = DialogUi.Button("Import font…");
        import.Margin = new Thickness(10, 0, 8, 8);
        import.HorizontalAlignment = HorizontalAlignment.Left;
        import.Click += (_, _) =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Import a font",
                Filter = "Font files (*.ttf;*.otf)|*.ttf;*.otf",
                Multiselect = false,
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var fontsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Winotch", "Fonts");
                Directory.CreateDirectory(fontsDir);
                var dest = Path.Combine(fontsDir, Path.GetFileName(dialog.FileName));
                var counter = 1;
                while (File.Exists(dest))
                {
                    dest = Path.Combine(fontsDir,
                        $"{Path.GetFileNameWithoutExtension(dialog.FileName)} ({counter++}){Path.GetExtension(dialog.FileName)}");
                }
                File.Copy(dialog.FileName, dest);
                var fontId = FontChoice.CustomId(dest);
                var customList = (ScopedSettings.Appearance.CustomFonts ?? new List<string>())
                    .Append(dest)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                UpdateAppearance(x => x with { Font = fontId, CustomFonts = customList });
                current = fontId;
                applyingPicker = true;
                picker.Items.Add(new FontOption(fontId,
                    (FontChoice.CustomFamily(dest) ?? Path.GetFileNameWithoutExtension(dest)) + "  (imported)"));
                picker.SelectedIndex = picker.Items.Count - 1;
                applyingPicker = false;
                Paint();
            }
            catch (IOException)
            {
                MessageBox.Show(this, "Couldn't copy the font file into the Winotch fonts folder.",
                    "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Couldn't copy the font file into the Winotch fonts folder.",
                    "Import failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };

        stack.Children.Add(picker);
        stack.Children.Add(import);
        return MakeSettingRow("Font", "Click a tile, pick an installed font, or import a .ttf / .otf", stack);
    }

    /// <summary>Accent swatches + custom hex + follow-Windows toggle.</summary>
    private Border MakeAccentRow(string label, string sub, AppearanceSettings appearance)
    {
        var wrap = new WrapPanel { Margin = new Thickness(10, 4, 8, 4) };
        var swatches = new List<Border>();

        void RefreshAccentSelection()
        {
            var currentSettings = ScopedSettings.Appearance;
            for (var i = 0; i < swatches.Count; i++)
            {
                var selected = currentSettings.Accent == AccentMode.Custom &&
                               string.Equals(ThemeService.ParseHex(ThemeService.AccentSwatches[i]).ToHex(),
                                   ThemeService.ParseHex(currentSettings.AccentHex).ToHex(),
                                   StringComparison.OrdinalIgnoreCase);
                swatches[i].BorderBrush = selected
                    ? (Brush)FindResource("NotchAccent")
                    : (Brush)FindResource("NotchStroke");
                swatches[i].BorderThickness = new Thickness(selected ? 2 : 1);
            }
        }

        var hexBox = new TextBox
        {
            Text = appearance.AccentHex,
            Width = 76,
            FontFamily = new FontFamily("Consolas, Courier New"),
            FontSize = 11.5,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = (Brush)FindResource("NotchPanelHover"),
            Foreground = (Brush)FindResource("NotchText"),
            BorderBrush = (Brush)FindResource("NotchStroke"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 6, 6),
        };

        foreach (var hex in ThemeService.AccentSwatches)
        {
            var color = ThemeService.ParseHex(hex).ToWpf();
            var swatch = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(color),
                BorderBrush = (Brush)FindResource("NotchStroke"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                hexBox.Text = hex;
                appearance = appearance with { AccentHex = hex };
                UpdateAppearance(x => x with { Accent = AccentMode.Custom, AccentHex = hex });
                RefreshAccentSelection();
            };
            swatches.Add(swatch);
            wrap.Children.Add(swatch);
        }
        void ApplyHex()
        {
            var parsed = ThemeService.ParseHex(hexBox.Text.Trim());
            if (string.Equals(parsed.ToHex(), ThemeService.ParseHex(appearance.AccentHex).ToHex(),
                    StringComparison.OrdinalIgnoreCase))
            {
                appearance = appearance with { AccentHex = parsed.ToHex() };
            }
            hexBox.Text = parsed.ToHex();
            UpdateAppearance(x => x with { Accent = AccentMode.Custom, AccentHex = parsed.ToHex() });
            RefreshAccentSelection();
        }
        hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) ApplyHex(); };
        hexBox.LostFocus += (_, _) => ApplyHex();
        wrap.Children.Add(hexBox);

        var follow = new CheckBox
        {
            Content = "Follow Windows accent",
            IsChecked = appearance.Accent == AccentMode.FollowOs,
            FontSize = 11.5,
            Foreground = (Brush)FindResource("NotchText"),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 6),
        };
        follow.Checked += (_, _) =>
        {
            UpdateAppearance(x => x with { Accent = AccentMode.FollowOs });
            RefreshAccentSelection();
        };
        follow.Unchecked += (_, _) =>
        {
            UpdateAppearance(x => x with { Accent = AccentMode.Default });
            RefreshAccentSelection();
        };
        wrap.Children.Add(follow);

        RefreshAccentSelection();
        return MakeSettingRow(label, sub, wrap);
    }

    /// <summary>A labeled slider with a live value readout.</summary>
    private Border MakeSliderRow(string label, string sub, int min, int max, int current,
        Action<double> onChanged)
    {
        var grid = new Grid { Margin = new Thickness(10, 4, 8, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = current,
            IsSnapToTickEnabled = true,
            TickFrequency = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("NotchAccent"),
        };
        var valueText = new TextBlock
        {
            Text = $"{current}",
            FontSize = 11,
            FontFamily = new FontFamily("Consolas, Courier New"),
            Foreground = (Brush)FindResource("NotchMutedText"),
            Margin = new Thickness(10, 0, 2, 0),
            MinWidth = 28,
            TextAlignment = TextAlignment.Right,
        };
        slider.ValueChanged += (_, e) =>
        {
            valueText.Text = $"{(int)e.NewValue}";
            onChanged(e.NewValue);
        };
        Grid.SetColumn(slider, 0);
        Grid.SetColumn(valueText, 1);
        grid.Children.Add(slider);
        grid.Children.Add(valueText);

        return MakeSettingRow(label, sub, grid);
    }

    /// <summary>A font picker entry: the persisted font id plus its label.</summary>
    private sealed record FontOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private Style? _rowHoverStyle;

    private Style RowHoverStyle
    {
        get
        {
            if (_rowHoverStyle is null)
            {
                var style = new Style(typeof(Border));
                var hover = new Trigger { Property = Border.IsMouseOverProperty, Value = true };
                hover.Setters.Add(new Setter(Border.BackgroundProperty, (Brush)FindResource("NotchPanelHover")));
                style.Triggers.Add(hover);
                _rowHoverStyle = style;
            }
            return _rowHoverStyle;
        }
    }
}

internal static class ColHexExtensions
{
    public static string ToHex(this Col color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color ToWpf(this Col color) => Color.FromArgb(color.A, color.R, color.G, color.B);
}