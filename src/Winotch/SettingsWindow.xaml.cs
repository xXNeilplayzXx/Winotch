using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Winotch.Engine;

namespace Winotch;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private readonly Action? _rerunSetup;

    public SettingsWindow(SettingsService settings, StartupService startup, Action? rerunSetup = null)
    {
        _settings = settings;
        _startup = startup;
        _rerunSetup = rerunSetup;
        InitializeComponent();
        VersionText.Text = AssemblyVersion();
        BuildSections();
    }

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
        SetBrush("NotchPanel", theme.WpfPanelTop);
        SetBrush("NotchPanelRaised", theme.WpfCard);
        SetBrush("NotchPanelHover", theme.WpfCardHover);
        SetBrush("NotchStroke", theme.WpfStroke);
        SetBrush("NotchAccent", theme.WpfAccent);
        SetBrush("NotchText", theme.WpfText);
        SetBrush("NotchMutedText", theme.WpfMuted);
        SetBrush("NotchHairline", theme.WpfHairline);
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
        _opacityRow.Visibility = s.Appearance.Glass ? Visibility.Visible : Visibility.Collapsed;
        _blurRow.Visibility = s.Appearance.Glass ? Visibility.Visible : Visibility.Collapsed;

        var generalRows = new List<FrameworkElement>
        {
            MakeToggleRow("24-hour clock", "Show times in 24h format",
                s.General.Use24HourClock,
                v => _settings.Update(x => x with { General = x.General with { Use24HourClock = v } })),
            MakeToggleRow("Show the date", "Day + month in the pill and clock widget",
                s.General.ShowDate,
                v => _settings.Update(x => x with { General = x.General with { ShowDate = v } })),
        };
        if (_rerunSetup is not null)
        {
            generalRows.Add(MakeButtonRow("Rerun setup wizard", "Reopen the two-page setup", _rerunSetup));
        }
        generalRows.Add(MakeToggleRow("Attach to the top edge", "Unchecked = floating island below the edge",
            s.General.Style == NotchStyle.Notch,
            v => _settings.Update(x => x with { General = x.General with { Style = v ? NotchStyle.Notch : NotchStyle.Island } })));
        generalRows.Add(MakeToggleRow("Start with Windows", "Launch at sign-in from the tray",
            s.General.StartWithWindows,
            v =>
            {
                _settings.Update(x => x with { General = x.General with { StartWithWindows = v } });
                var executable = StartupService.CurrentExecutablePath();
                _startup.SetEnabled(v, executable);
            }));
        AddSection("General", generalRows.ToArray());

        AddSection("Appearance",
            MakeChoiceRow("Theme", "System follows your Windows app theme live",
                new[]
                {
                    ("dark", "Dark"), ("light", "Light"), ("system", "System"), ("color", "Color"),
                }, s.Appearance.Theme.ToString().ToLowerInvariant(),
                v => UpdateAppearance(x => x with { Theme = ParseTheme(v) })),
            MakeAccentRow("Accent", "The highlight color — preset, custom, or Windows",
                s.Appearance),
            MakeChoiceRow("Gradient", "A background skin over the island body",
                GradientSkin.Known.Select(g => (g, GradientSkin.Labels[g])).ToArray(),
                s.Appearance.Gradient,
                v => UpdateAppearance(x => x with { Gradient = v })),
            MakeChoiceRow("Surface", "Glass is translucent with live blur; Solid is opaque",
                new[] { ("glass", "Glass"), ("solid", "Solid") },
                s.Appearance.Glass ? "glass" : "solid",
                v =>
                {
                    UpdateAppearance(x => x with { Glass = v == "glass" });
                    _opacityRow.Visibility = v == "glass" ? Visibility.Visible : Visibility.Collapsed;
                    _blurRow.Visibility = v == "glass" ? Visibility.Visible : Visibility.Collapsed;
                }),
            _opacityRow,
            _blurRow);

        AddSection("Size", MakeSizeControl(s.Appearance));

        AddSection("Display", MakeDisplayControl(s.Appearance));

        _customIdleRow = MakeTextBoxRow("Custom idle text", "Shown in the pill when 'Custom text' is selected",
            s.Appearance.CustomIdleText,
            text => UpdateAppearance(x => x with { CustomIdleText = text }));
        _customIdleRow.Visibility = s.Appearance.IdleContent == IdleContentMode.Custom
            ? Visibility.Visible
            : Visibility.Collapsed;

        AddSection("Pill",
            MakeChoiceRow("Idle content", "What the collapsed pill shows when nothing is playing",
                new[]
                {
                    ("auto", "Default"), ("brandclock", "Brand + clock"),
                    ("clockonly", "Clock only"), ("custom", "Custom text"), ("dotonly", "Dot only"),
                }, IdleKey(s.Appearance.IdleContent),
                v =>
                {
                    UpdateAppearance(x => x with { IdleContent = ParseIdle(v) });
                    _customIdleRow.Visibility = v == "custom" ? Visibility.Visible : Visibility.Collapsed;
                }),
            _customIdleRow,
            MakeToggleRow("Drag to move", "Grab the pill to move it; the spot is remembered",
                s.Appearance.DragToMove,
                v => UpdateAppearance(x => x with { DragToMove = v })));

        AddSection("Alerts",
            MakeToggleRow("Media toasts", "Pop a toast when the track changes",
                s.Toasts.MediaToastsEnabled,
                v => _settings.Update(x => x with { Toasts = x.Toasts with { MediaToastsEnabled = v } })),
            MakeToggleRow("Priority alerts", "Low battery, charger, Wi-Fi, mic / camera events",
                s.Features.PriorityAlertsEnabled,
                v => _settings.Update(x => x with { Features = x.Features with { PriorityAlertsEnabled = v } })));

        AddSection("Widgets & features",
            MakeToggleRow("Clipboard history", "In-memory last 10 items — never written to disk",
                s.Features.ClipboardHistoryEnabled,
                v => _settings.Update(x => x with { Features = x.Features with { ClipboardHistoryEnabled = v } })),
            MakeToggleRow("Focus timer", "25 / 5 and 50 / 10 sessions with breaks",
                s.Features.FocusTimerEnabled,
                v => _settings.Update(x => x with { Features = x.Features with { FocusTimerEnabled = v } })));
    }

    private void UpdateAppearance(Func<AppearanceSettings, AppearanceSettings> change) =>
        _settings.Update(x => x with { Appearance = change(x.Appearance) });

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
        _ => ThemeMode.Dark,
    };

    // ------------------------------------------------------------- sections

    private void AddSection(string title, params FrameworkElement[] rows)
    {
        Rows.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            Style = (Style)FindResource("SectionLabelText"),
            Margin = new Thickness(2, 0, 0, 8),
        });

        var card = new Border
        {
            Background = (Brush)FindResource("NotchPanelRaised"),
            BorderBrush = (Brush)FindResource("NotchStroke"),
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
                    Background = (Brush)FindResource("NotchHairline"),
                    Margin = new Thickness(10, 0, 10, 0),
                });
            }
        }
        card.Child = stack;
        Rows.Children.Add(card);
    }

    // ------------------------------------------------------------- toggles

    private Border MakeToggleRow(string label, string sub, bool initial, Action<bool> onChanged)
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
            text.Children.Add(new TextBlock
            {
                Text = sub,
                FontSize = 11,
                Foreground = (Brush)FindResource("NotchMutedText"),
                Margin = new Thickness(0, 1, 0, 0),
            });
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
                var selected = options[i].Value == mode;
                chips[i].Background = selected
                    ? (Brush)FindResource("NotchAccent")
                    : (Brush)FindResource("NotchPanelHover");
                chipLabels[i].Foreground = selected
                    ? new SolidColorBrush(Color.FromRgb(0x06, 0x13, 0x1A))
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

        void PaintChips()
        {
            for (var i = 0; i < chips.Count; i++)
            {
                var selected = string.Equals(presetOptions[i].Id, presetId, StringComparison.OrdinalIgnoreCase);
                chips[i].Background = selected
                    ? (Brush)FindResource("NotchAccent")
                    : (Brush)FindResource("NotchPanelHover");
                chipLabels[i].Foreground = selected
                    ? new SolidColorBrush(Color.FromRgb(0x06, 0x13, 0x1A))
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
                presetId = preset.Id;
                draftWidth = preset.Width;
                draftHeight = preset.Height;
                UpdateAppearance(x => x with
                {
                    CustomSize = true,
                    Width = preset.Width,
                    Height = preset.Height,
                    SizePreset = preset.Id,
                });
            }
            SetSliders();
            PaintChips();
        }

        var wrap = new WrapPanel { Margin = new Thickness(10, 4, 8, 4) };
        var presetOptions = new[] { ("", "Auto") }
            .Concat(SizePresets.All.Select(p => (p.Id, p.Label)))
            .ToArray();
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
                    ? new SolidColorBrush(Color.FromRgb(0x06, 0x13, 0x1A))
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

    /// <summary>Accent swatches + custom hex + follow-Windows toggle.</summary>
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
                swatches[i].BorderBrush = selected
                    ? (Brush)FindResource("NotchAccent")
                    : (Brush)FindResource("NotchStroke");
                swatches[i].BorderThickness = new Thickness(selected ? 2 : 1);
            }
        }

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