using System.IO;
using Path = System.IO.Path;
using Avalonia.Controls.Shapes;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Winotch.Engine;

namespace Winotch;

public partial class SettingsWindow : Avalonia.Controls.Window
{
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly Action? _rerunSetup;
    private readonly Action? _factoryReset;
    private readonly Action? _enterDragMode;
    private readonly Action? _checkForUpdates;

    public SettingsWindow(SettingsService settings, StartupService startup,
        Action? rerunSetup = null, Action? factoryReset = null, Action? enterDragMode = null,
        Action? checkForUpdates = null)
    {
        _settings = settings;
        _startup = startup;
        _rerunSetup = rerunSetup;
        _factoryReset = factoryReset;
        _enterDragMode = enterDragMode;
        _checkForUpdates = checkForUpdates;

        Title = "Winotch — Settings";
        Width = 470;
        MinHeight = 540;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Res("NotchPanel");

        var root = new Grid { Margin = new Thickness(26, 22, 26, 20) };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        // Header
        var header = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        var headerLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        headerLeft.Children.Add(new Ellipse { Width = 11, Height = 11, Fill = Res("NotchAccent"), VerticalAlignment = VerticalAlignment.Center });
        headerLeft.Children.Add(new TextBlock { Text = "Winotch", FontSize = 23, FontWeight = FontWeight.SemiBold, Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        headerLeft.Children.Add(new TextBlock { Text = "Settings", FontSize = 23, Foreground = Res("NotchMutedText"), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        _versionBadge = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Res("NotchPanelRaised"),
            BorderBrush = Res("NotchStroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(10, 4),
        };
        _versionText = new TextBlock { FontSize = 11, Foreground = Res("NotchMutedText"), FontFamily = new FontFamily("Consolas, Courier New, monospace") };
        _versionBadge.Child = _versionText;
        _versionText.Text = AssemblyVersion();
        header.Children.Add(headerLeft);
        header.Children.Add(_versionBadge);

        // Content
        _rows = new StackPanel();
        var scroller = new ScrollViewer { Content = _rows };
        ScrollViewer.SetVerticalScrollBarVisibility(scroller, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        // Footer
        var footer = new Border
        {
            Background = Res("NotchPanelRaised"),
            BorderBrush = Res("NotchStroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(13, 9),
            Margin = new Thickness(0, 16, 0, 0),
        };
        var footerStack = new StackPanel();
        var footerInfo = new StackPanel { Orientation = Orientation.Horizontal };
        footerInfo.Children.Add(new TextBlock
        {
            Text = "\u2713", FontSize = 11, Foreground = Res("NotchAccent"),
            VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 9, 0),
        });
        var footerText = new StackPanel();
        footerText.Children.Add(new TextBlock { Text = "Settings apply live", FontSize = 11, Foreground = Res("NotchText"), FontWeight = FontWeight.SemiBold });
        footerText.Children.Add(new TextBlock
        {
            Text = "Stored as JSON — settings persist across sessions",
            FontSize = 10.5, Foreground = Res("NotchMutedText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
        });
        footerInfo.Children.Add(footerText);
        _footerActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        footerStack.Children.Add(footerInfo);
        footerStack.Children.Add(_footerActions);
        footer.Child = footerStack;

        Grid.SetRow(header, 0);
        Grid.SetRow(scroller, 1);
        Grid.SetRow(footer, 2);
        root.Children.Add(header);
        root.Children.Add(scroller);
        root.Children.Add(footer);

        Content = root;
        BuildSections();
        BuildFooterActions();
    }

    private readonly Border _versionBadge;
    private readonly TextBlock _versionText;
    private readonly StackPanel _rows;
    private readonly StackPanel _footerActions;

    /// <summary>Sticky footer actions: reset everything to defaults, or wipe
    /// and restart fresh.</summary>
    private void BuildFooterActions()
    {
        var reset = DialogUi.Button("Reset to defaults");
        reset.Margin = new Thickness(0, 0, 8, 0);
        reset.Click += async (_, _) =>
        {
            if (!await NotchMessageBox.Confirm(this,
                "Reset every Winotch setting back to its default?",
                "Reset to defaults"))
                return;
            _settings.Update(_ => WinotchSettings.Defaults);
            _rows.Children.Clear();
            BuildSections();
        };
        var factory = DialogUi.Button("Factory reset…", danger: true);
        factory.Click += async (_, _) =>
        {
            if (!await NotchMessageBox.Confirm(this,
                "Factory reset wipes ALL settings (notes, apps, fonts, layout) and restarts Winotch fresh. Continue?",
                "Factory reset"))
                return;
            _factoryReset?.Invoke();
        };
        _footerActions.Children.Add(reset);
        _footerActions.Children.Add(factory);
    }

    private static string AssemblyVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "" : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>Re-skin every surface from the resolved shell theme.</summary>
    public void ApplyTheme(ShellTheme theme, bool rebuild = false)
    {
        DialogUi.Current = theme;
        Background = Res("NotchPanel");
        SetBrush("NotchPanel", theme.AvaloniaPanelTop);
        SetBrush("NotchPanelRaised", theme.AvaloniaCard);
        SetBrush("NotchPanelHover", theme.AvaloniaCardHover);
        SetBrush("NotchStroke", theme.AvaloniaStroke);
        SetBrush("NotchAccent", theme.AvaloniaAccent);
        SetBrush("NotchText", theme.AvaloniaText);
        SetBrush("NotchMutedText", theme.AvaloniaMuted);
        SetBrush("NotchHairline", theme.AvaloniaHairline);
        if (rebuild)
        {
            _rows.Children.Clear();
            BuildSections();
        }
    }

    private static void SetBrush(string key, Color color)
    {
        if (Application.Current?.Resources[key] is IBrush)
            Application.Current.Resources[key] = new SolidColorBrush(color);
    }

    private static IBrush Res(string key) =>
        Application.Current?.FindResource(key) as IBrush ?? new SolidColorBrush(Colors.Transparent);

    private Border _opacityRow = null!;
    private Border _blurRow = null!;
    private Border _blurEnabledRow = null!;
    private Border _customIdleRow = null!;

    private void BuildSections()
    {
        var s = _settings.Current;

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
                _blurRow.IsVisible = v;
            });
        _blurEnabledRow.IsVisible = s.Appearance.Glass;
        _opacityRow.IsVisible = s.Appearance.Glass;
        _blurRow.IsVisible = s.Appearance.Glass && s.Appearance.BlurEnabled;

        var generalRows = new List<Control>
        {
            MakeToggleRow("24-hour clock", "Show times in 24h format",
                s.General.Use24HourClock,
                v => _settings.Update(x => x with { General = x.General with { Use24HourClock = v } })),
            MakeToggleRow("Show the date", "Day + month in the panel and clock widget",
                s.General.ShowDate,
                v => _settings.Update(x => x with { General = x.General with { ShowDate = v } })),
            MakeToggleRow("Date on the pill", "Also show the day + month on the collapsed pill",
                s.General.ShowDateOnPill,
                v => _settings.Update(x => x with { General = x.General with { ShowDateOnPill = v } })),
            MakeChoiceRow("Clock layout", "Stacked centers the date under the time",
                new[] { ("standard", "Standard"), ("stacked", "Stacked") },
                s.General.Layout == ClockLayout.Stacked ? "stacked" : "standard",
                v => _settings.Update(x => x with
                {
                    General = x.General with { Layout = v == "stacked" ? ClockLayout.Stacked : ClockLayout.Standard },
                })),
        };
        if (_rerunSetup is not null)
        {
            generalRows.Add(MakeButtonRow("Rerun setup wizard", "Reopen the two-page setup", _rerunSetup));
        }
        generalRows.Add(MakeToggleRow("Attach to the top edge", "Unchecked = floating island below the edge",
            s.General.Style == NotchStyle.Notch,
            v => _settings.Update(x => x with { General = x.General with { Style = v ? NotchStyle.Notch : NotchStyle.Island } })));
        generalRows.Add(MakeSliderRow("Drop from top", "0 = tucked under the top edge — or just drag the pill",
            0, 200, (int)Math.Clamp(s.Appearance.OffsetY, 0, 200),
            v => UpdateAppearance(x => x with { OffsetY = v })));
        generalRows.Add(MakeToggleRow("Start with system", "Automatically launch when you sign in",
            s.General.StartWithWindows,
            v =>
            {
                _settings.Update(x => x with { General = x.General with { StartWithWindows = v } });
                var executable = StartupService.CurrentExecutablePath();
                _startup.SetEnabled(v, executable);
            }));
        generalRows.Add(MakeToggleRow("Check for updates automatically",
            "Look for newer builds on GitHub when Winotch starts",
            s.General.AutoCheckUpdates,
            v => _settings.Update(x => x with { General = x.General with { AutoCheckUpdates = v } })));
        if (_checkForUpdates is not null)
        {
            generalRows.Add(MakeButtonRow("Check for updates",
                "See if a newer build is available", _checkForUpdates));
        }
        AddSection("General", generalRows.ToArray());

        AddSection("Appearance",
            MakeChoiceRow("Theme", "System follows OS; Colour tints the island; Background follows your wallpaper",
                new[]
                {
                    ("dark", "Dark"), ("light", "Light"), ("system", "System"),
                    ("color", "Colour"), ("background", "Background"),
                }, s.Appearance.Theme.ToString().ToLowerInvariant(),
                v => UpdateAppearance(x => x with { Theme = ParseTheme(v) })),
            MakeAccentRow("Accent", "The highlight color — preset, custom, or follow OS",
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
                    var blurEnabled = _settings.Current.Appearance.BlurEnabled;
                    _blurEnabledRow.IsVisible = glass;
                    _opacityRow.IsVisible = glass;
                    _blurRow.IsVisible = glass && blurEnabled;
                }),
            _blurEnabledRow,
            _opacityRow,
            _blurRow);

        AddSection("Size", MakeSizeControl(s.Appearance));
        AddSection("Display", MakeDisplayControl(s.Appearance));

        _customIdleRow = MakeTextBoxRow("Custom idle text", "Shown in the pill when 'Custom text' is selected",
            s.Appearance.CustomIdleText,
            text => UpdateAppearance(x => x with { CustomIdleText = text }));
        _customIdleRow.IsVisible = s.Appearance.IdleContent == IdleContentMode.Custom;

        var pillRows = new List<Control>
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
                    _customIdleRow.IsVisible = v == "custom";
                }),
            _customIdleRow,
            MakeToggleRow("Drag to move", "Grab the pill to move it; the spot is remembered",
                s.Appearance.DragToMove,
                v => UpdateAppearance(x => x with { DragToMove = v })),
            MakeChoiceRow("Edge attachment", "Top = the usual pill; Left / Right rotate it vertically onto that edge",
                new[] { ("top", "Top"), ("left", "Left"), ("right", "Right") },
                EdgeKey(s.General.Edge),
                v =>
                {
                    var edge = ParseEdge(v);
                    var appearance = edge == _settings.Current.General.Edge
                        ? _settings.Current.Appearance
                        : _settings.Current.Appearance with { OffsetX = 0, OffsetY = 0 };
                    _settings.Update(x => x with
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
                v => _settings.Update(x => x with
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
        pillRows.Add(MakeToggleRow("Calls on the pill", "Green bubble while you're on a call",
            s.Features.ShowCallsOnPill,
            v => _settings.Update(x => x with { Features = x.Features with { ShowCallsOnPill = v } })));
        pillRows.Add(MakeToggleRow("Now playing on the pill", "Track chip while music plays",
            s.Features.ShowMediaOnPill,
            v => _settings.Update(x => x with { Features = x.Features with { ShowMediaOnPill = v } })));
        pillRows.Add(MakeToggleRow("Focus timer on the pill", "Countdown chip while a session runs",
            s.Features.ShowFocusOnPill,
            v => _settings.Update(x => x with { Features = x.Features with { ShowFocusOnPill = v } })));
        pillRows.Add(MakeToggleRow("Battery on the pill", "Battery percent in the default idle layout",
            s.Features.ShowBatteryOnPill,
            v => _settings.Update(x => x with { Features = x.Features with { ShowBatteryOnPill = v } })));
        AddSection("Pill", pillRows.ToArray());

        AddSection("Alerts",
            MakeToggleRow("Now-playing toasts", "Pop a toast when the track changes",
                s.Toasts.MediaToastsEnabled,
                v => _settings.Update(x => x with { Toasts = x.Toasts with { MediaToastsEnabled = v } })),
            MakeToggleRow("Priority alerts", "Low battery, charger, Wi-Fi, mic / camera events",
                s.Features.PriorityAlertsEnabled,
                v => _settings.Update(x => x with { Features = x.Features with { PriorityAlertsEnabled = v } })),
            MakeToggleRow("Rain alerts", "Toast when precipitation starts within 30 minutes",
                s.Toasts.RainAlertsEnabled,
                v => _settings.Update(x => x with { Toasts = x.Toasts with { RainAlertsEnabled = v } })));

        AddSection("Widgets & features",
            MakeToggleRow("Clipboard history", "In-memory last 10 items — never written to disk",
                s.Features.ClipboardHistoryEnabled,
                v => _settings.Update(x => x with { Features = x.Features with { ClipboardHistoryEnabled = v } })),
            MakeToggleRow("Focus timer", "Sessions with breaks — pick a preset or a custom ratio",
                s.Features.FocusTimerEnabled,
                v => _settings.Update(x => x with { Features = x.Features with { FocusTimerEnabled = v } })));
    }

    private void UpdateAppearance(Func<AppearanceSettings, AppearanceSettings> change) =>
        _settings.Update(x => x with { Appearance = change(x.Appearance) });

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

    // ------------------------------------------------------------- sections

    private void AddSection(string title, params Control[] rows)
    {
        _rows.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            Classes = { "section-label" },
            Margin = new Thickness(2, 0, 0, 8),
        });

        var card = new Border
        {
            Background = Res("NotchPanelRaised"),
            BorderBrush = Res("NotchStroke"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(4, 4, 4, 4),
            Margin = new Thickness(0, 0, 0, 16),
        };

        var stack = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            stack.Children.Add(rows[i]);
            if (i < rows.Length - 1)
            {
                stack.Children.Add(new Border
                {
                    Height = 1,
                    Background = Res("NotchHairline"),
                    Margin = new Thickness(10, 0, 10, 0),
                });
            }
        }
        card.Child = stack;
        _rows.Children.Add(card);
    }

    // ------------------------------------------------------------- toggles

    private Border MakeToggleRow(string label, string sub, bool initial, Action<bool> onChanged)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
        });
        if (!string.IsNullOrEmpty(sub))
        {
            text.Children.Add(new TextBlock
            {
                Text = sub,
                FontSize = 11,
                Foreground = Res("NotchMutedText"),
                Margin = new Thickness(0, 1, 0, 0),
            });
        }

        var toggle = new CheckBox
        {
            IsChecked = initial,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        toggle.IsCheckedChanged += (_, _) => onChanged(toggle.IsChecked == true);

        Grid.SetColumn(text, 0);
        Grid.SetColumn(toggle, 1);
        grid.Children.Add(text);
        grid.Children.Add(toggle);

        return MakeHoverBorder(grid);
    }

    // --------------------------------------------------------------- rows

    private Border MakeSettingRow(string label, string sub, Control control)
    {
        var header = new StackPanel { Margin = new Thickness(10, 9, 8, 2) };
        header.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
        });
        if (!string.IsNullOrEmpty(sub))
        {
            header.Children.Add(new TextBlock
            {
                Text = sub,
                FontSize = 11,
                Foreground = Res("NotchMutedText"),
                Margin = new Thickness(0, 1, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        }

        var stack = new StackPanel { Children = { header, control } };
        return MakeHoverBorder(stack);
    }

    private static Border MakeHoverBorder(Control child)
    {
        var border = new Border
        {
            Child = child,
            Padding = new Thickness(10, 9, 8, 9),
            Margin = new Thickness(0, 1, 0, 1),
            CornerRadius = new CornerRadius(8),
        };
        var defaultBg = border.Background;
        border.PointerEntered += (_, _) => border.Background = Res("NotchPanelHover");
        border.PointerExited += (_, _) => border.Background = defaultBg;
        return border;
    }

    // ------------------------------------------------------------ display

    private Control MakeDisplayControl(AppearanceSettings appearance)
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
        Border? mapCard = null;

        void Paint()
        {
            for (var i = 0; i < chips.Count; i++)
            {
                var selected = options[i].Item1 == mode;
                chips[i].Background = selected ? Res("NotchAccent") : Res("NotchPanelHover");
                chipLabels[i].Foreground = selected
                    ? new SolidColorBrush(Color.FromRgb(0x06, 0x13, 0x1A))
                    : Res("NotchText");
            }
            if (mapCard is not null)
            {
                mapCard.IsVisible = mode == DisplayMode.Named;
            }
        }

        var wrap = new WrapPanel { Margin = new Thickness(10, 4, 8, 4) };
        foreach (var (value, labelText) in options)
        {
            var text = new TextBlock { Text = labelText, FontSize = 11.5, FontWeight = FontWeight.SemiBold };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(11, 4, 11, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = text,
            };
            chip.PointerPressed += (_, _) =>
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
            Background = Res("NotchPanelHover"),
            Margin = new Thickness(10, 0, 8, 8),
            Height = 140,
        };
        var hint = new TextBlock
        {
            Text = "Click a display",
            FontSize = 11.5,
            Foreground = Res("NotchMutedText"),
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
                    Background = Res("NotchPanelHover"),
                    BorderBrush = string.Equals(selectedDevice, monitor.DeviceName, StringComparison.OrdinalIgnoreCase)
                        ? Res("NotchAccent") : Res("NotchStroke"),
                    BorderThickness = new Thickness(string.Equals(selectedDevice, monitor.DeviceName, StringComparison.OrdinalIgnoreCase) ? 2 : 1),
                    CornerRadius = new CornerRadius(6),
                    Margin = new Thickness((monitor.Bounds.Left - minX) * scale, (monitor.Bounds.Top - minY) * scale, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock
                    {
                        Text = $"{name}\n{monitor.Bounds.Width}×{monitor.Bounds.Height}",
                        FontSize = 9.5,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = Res("NotchText"),
                    },
                };
                var deviceName = monitor.DeviceName;
                button.PointerPressed += (_, _) => onPicked(deviceName);
                map.Children.Add(button);
            }
        }

        return new Border { Child = map, CornerRadius = new CornerRadius(10) };
    }

    // --------------------------------------------------------------- size

    private Control MakeSizeControl(AppearanceSettings appearance)
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
        };
        var heightSlider = new Slider
        {
            Minimum = 30, Maximum = 200, Value = draftHeight,
            IsSnapToTickEnabled = true, TickFrequency = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var widthValue = new TextBlock { FontSize = 11, FontFamily = new FontFamily("Consolas, Courier New, monospace"), Foreground = Res("NotchMutedText"), MinWidth = 28, TextAlignment = TextAlignment.Right, Margin = new Thickness(10, 0, 2, 0) };
        var heightValue = new TextBlock { FontSize = 11, FontFamily = new FontFamily("Consolas, Courier New, monospace"), Foreground = Res("NotchMutedText"), MinWidth = 28, TextAlignment = TextAlignment.Right, Margin = new Thickness(10, 0, 2, 0) };

        void PaintChips()
        {
            for (var i = 0; i < chips.Count; i++)
            {
                var selected = string.Equals(presetOptions[i].Item1, presetId, StringComparison.OrdinalIgnoreCase);
                chips[i].Background = selected ? Res("NotchAccent") : Res("NotchPanelHover");
                chipLabels[i].Foreground = selected
                    ? new SolidColorBrush(Color.FromRgb(0x06, 0x13, 0x1A)) : Res("NotchText");
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
                    CustomSize = true, Width = preset.Value.Width, Height = preset.Value.Height, SizePreset = preset.Value.Id,
                });
            }
            SetSliders();
            PaintChips();
        }

        var wrap = new WrapPanel { Margin = new Thickness(10, 4, 8, 4) };
        foreach (var (id, label) in presetOptions)
        {
            var text = new TextBlock { Text = label, FontSize = 11.5, FontWeight = FontWeight.SemiBold };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(11, 4, 11, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = text,
            };
            chip.PointerPressed += (_, _) => ApplyPreset(id);
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
            CustomSize = true, Width = draftWidth, Height = draftHeight,
            SizePreset = SizePresets.Closest(draftWidth, draftHeight).Id,
        });

        void ResetDraft()
        {
            var committed = _settings.Current.Appearance;
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
        ((Control)buttons.Children[0]).Margin = new Thickness(0, 0, 8, 0);

        stack.Children.Add(MakeSliderRow("Width", "150–800 px (staged until Apply)", widthSlider, widthValue));
        stack.Children.Add(MakeSliderRow("Height", "30–200 px (staged until Apply)", heightSlider, heightValue));
        stack.Children.Add(buttons);

        return MakeSettingRow("Size", "Presets apply instantly; sliders stage a draft (Apply / Reset)", stack);
    }

    private Border MakeSliderRow(string label, string sub, Slider slider, TextBlock valueText)
    {
        var grid = new Grid { Margin = new Thickness(10, 4, 8, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
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
            Background = Res("NotchPanelHover"),
            Foreground = Res("NotchText"),
            BorderBrush = Res("NotchStroke"),
            BorderThickness = new Thickness(1),
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Return) onChanged(box.Text?.Trim() ?? "");
        };
        box.LostFocus += (_, _) => onChanged(box.Text?.Trim() ?? "");
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
                chips[i].Background = selected ? Res("NotchAccent") : Res("NotchPanelHover");
                labels[i].Foreground = selected
                    ? new SolidColorBrush(Color.FromRgb(0x06, 0x13, 0x1A)) : Res("NotchText");
            }
        }

        foreach (var (value, optionLabel) in options)
        {
            var text = new TextBlock { Text = optionLabel, FontSize = 11.5, FontWeight = FontWeight.SemiBold };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(11, 4, 11, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = text,
            };
            chip.PointerPressed += (_, _) =>
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
                tiles[i].Background = selected ? Res("NotchAccent") : Res("NotchPanelHover");
                clockTexts[i].Foreground = selected
                    ? new SolidColorBrush(Color.FromRgb(0x06, 0x13, 0x1A)) : Res("NotchText");
                nameTexts[i].Foreground = selected
                    ? new SolidColorBrush(Color.FromRgb(0x06, 0x13, 0x1A)) : Res("NotchMutedText");
            }
        }

        var clock = DateTime.Now.ToString(_settings.Current.General.Use24HourClock ? "H:mm" : "h:mm");
        foreach (var (id, label) in FontChoice.Known)
        {
            var familyName = FontChoice.FamilyOf(id);
            var family = new FontFamily(familyName);
            var clockText = new TextBlock
            {
                Text = clock, FontSize = 19, FontWeight = FontWeight.SemiBold, FontFamily = family,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var nameText = new TextBlock
            {
                Text = label, FontSize = 9.5, FontFamily = family,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0),
            };
            var tile = new Border
            {
                Width = 88, Height = 60, CornerRadius = new CornerRadius(10),
                Background = Res("NotchPanelHover"), Margin = new Thickness(0, 0, 6, 6),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { clockText, nameText },
                },
            };
            var fontId = id;
            tile.PointerPressed += (_, _) =>
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

        // System fonts via Avalonia FontManager
        var systemFonts = Avalonia.Media.FontManager.Current.SystemFonts
            .Select(f => f.Name)
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
        import.Click += async (_, _) =>
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Import a font",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Font files") { Patterns = new[] { "*.ttf", "*.otf" } } },
            });
            if (files.Count == 0) return;
            var sourcePath = files[0].Path.LocalPath;
            try
            {
                var fontsDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Winotch", "Fonts");
                Directory.CreateDirectory(fontsDir);
                var dest = Path.Combine(fontsDir, Path.GetFileName(sourcePath));
                var counter = 1;
                while (File.Exists(dest))
                {
                    dest = Path.Combine(fontsDir,
                        $"{Path.GetFileNameWithoutExtension(sourcePath)} ({counter++}){Path.GetExtension(sourcePath)}");
                }
                File.Copy(sourcePath, dest);
                var fontId = FontChoice.CustomId(dest);
                var customList = (_settings.Current.Appearance.CustomFonts ?? new List<string>())
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
            catch (Exception)
            {
                _ = NotchMessageBox.Confirm(this, "Couldn't copy the font file into the Winotch fonts folder.", "Import failed");
            }
        };

        stack.Children.Add(picker);
        stack.Children.Add(import);
        return MakeSettingRow("Font", "Click a tile, pick an installed font, or import a .ttf / .otf", stack);
    }

    private Border MakeAccentRow(string label, string sub, AppearanceSettings appearance)
    {
        var wrap = new WrapPanel { Margin = new Thickness(10, 4, 8, 4) };
        var swatches = new List<Border>();

        void RefreshAccentSelection()
        {
            var currentSettings = _settings.Current.Appearance;
            for (var i = 0; i < swatches.Count; i++)
            {
                var selected = currentSettings.Accent == AccentMode.Custom &&
                               string.Equals(ThemeService.ParseHex(ThemeService.AccentSwatches[i]).ToHex(),
                                   ThemeService.ParseHex(currentSettings.AccentHex).ToHex(),
                                   StringComparison.OrdinalIgnoreCase);
                swatches[i].BorderBrush = selected ? Res("NotchAccent") : Res("NotchStroke");
                swatches[i].BorderThickness = new Thickness(selected ? 2 : 1);
            }
        }

        var hexBox = new TextBox
        {
            Text = appearance.AccentHex,
            Width = 76,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            FontSize = 11.5,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Res("NotchPanelHover"),
            Foreground = Res("NotchText"),
            BorderBrush = Res("NotchStroke"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 0, 6, 6),
        };

        foreach (var hex in ThemeService.AccentSwatches)
        {
            var color = ThemeService.ParseHex(hex);
            var swatch = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(color.ToAvalonia()),
                BorderBrush = Res("NotchStroke"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            swatch.PointerPressed += (_, _) =>
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
            var parsed = ThemeService.ParseHex(hexBox.Text?.Trim() ?? "");
            if (string.Equals(parsed.ToHex(), ThemeService.ParseHex(appearance.AccentHex).ToHex(),
                    StringComparison.OrdinalIgnoreCase))
            {
                appearance = appearance with { AccentHex = parsed.ToHex() };
            }
            hexBox.Text = parsed.ToHex();
            UpdateAppearance(x => x with { Accent = AccentMode.Custom, AccentHex = parsed.ToHex() });
            RefreshAccentSelection();
        }
        hexBox.KeyDown += (_, e) => { if (e.Key == Key.Return) ApplyHex(); };
        hexBox.LostFocus += (_, _) => ApplyHex();
        wrap.Children.Add(hexBox);

        var follow = new CheckBox
        {
            Content = new TextBlock { Text = "Follow OS accent" },
            IsChecked = appearance.Accent == AccentMode.FollowOs,
            FontSize = 11.5,
            Foreground = Res("NotchText"),
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 3, 0, 6),
        };
        follow.IsCheckedChanged += (_, _) =>
        {
            if (follow.IsChecked == true)
                UpdateAppearance(x => x with { Accent = AccentMode.FollowOs });
            else
                UpdateAppearance(x => x with { Accent = AccentMode.Default });
            RefreshAccentSelection();
        };
        wrap.Children.Add(follow);

        RefreshAccentSelection();
        return MakeSettingRow(label, sub, wrap);
    }

    private Border MakeSliderRow(string label, string sub, int min, int max, int current,
        Action<double> onChanged)
    {
        var grid = new Grid { Margin = new Thickness(10, 4, 8, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = current,
            IsSnapToTickEnabled = true, TickFrequency = 1,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var valueText = new TextBlock
        {
            Text = $"{current}", FontSize = 11,
            FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            Foreground = Res("NotchMutedText"), Margin = new Thickness(10, 0, 2, 0),
            MinWidth = 28, TextAlignment = TextAlignment.Right,
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

    private sealed record FontOption(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}

/// <summary>Simple message box replacement for Avalonia (no built-in MessageBox).</summary>
internal static class NotchMessageBox
{
    public static async Task<bool> Confirm(Avalonia.Controls.Window parent, string message, string title)
    {
        var dialog = new Avalonia.Controls.Window
        {
            Title = title,
            Width = 380,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = DialogUi.Panel,
            CanResize = false,
        };
        var result = false;
        var stack = new StackPanel { Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock
        {
            Text = message, FontSize = 12.5, Foreground = DialogUi.Text,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16),
        });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var cancel = DialogUi.Button("Cancel");
        cancel.Click += (_, _) => { result = false; dialog.Close(); };
        var ok = DialogUi.Button("OK", primary: true);
        ok.Margin = new Thickness(8, 0, 0, 0);
        ok.Click += (_, _) => { result = true; dialog.Close(); };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        dialog.Content = stack;
        await dialog.ShowDialog(parent);
        return result;
    }
}

internal static class ColHexExtensions
{
    public static string ToHex(this Col color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
