using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Winotch;

/// <summary>The two-page setup wizard: page 1 picks the look (theme, Notch vs
/// Island, Glass vs Solid); page 2 picks the display and arranges the widgets.
/// Opens on first run and from "Rerun setup…".</summary>
public sealed class SetupWindow : Avalonia.Controls.Window
{
    private readonly SettingsService _settings;
    private ThemeMode _theme;
    private NotchStyle _style;
    private bool _glass;
    private ExpandDirection _openDirection;
    private DisplayMode _display;
    private string _displayDevice = "";
    private List<string> _widgets;
    private List<LauncherApp> _launcherApps;
    private Border? _launcherCard;
    private WrapPanel? _launcherChips;
    private TextBlock? _launcherEmpty;
    private bool _launcherScanStarted;

    private readonly StackPanel _pageHost = new();
    private Border _displayMapCard = null!;
    private Border _previewHost = null!;
    private Border _previewPill = null!;
    private TextBlock _previewCaption = null!;

    public SetupWindow(SettingsService settings)
    {
        _settings = settings;
        _theme = settings.Current.Appearance.Theme;
        _style = settings.Current.General.Style;
        _glass = settings.Current.Appearance.Glass;
        _openDirection = settings.Current.General.OpenDirection;
        _display = settings.Current.Appearance.Display;
        _displayDevice = settings.Current.Appearance.DisplayDeviceName;
        _widgets = new List<string>(settings.Current.Widgets.Enabled);
        _launcherApps = new List<LauncherApp>(settings.Current.Widgets.LauncherApps);

        DialogUi.ApplyFrame(this, "Welcome to Winotch");
        Title = settings.WasCreated ? "Welcome to Winotch" : "Winotch — Setup";
        Width = 500;
        Height = 560;
        SizeToContent = SizeToContent.Manual;
        CanResize = false;

        Content = new ScrollViewer
        {
            Content = _pageHost,
        };
        ScrollViewer.SetVerticalScrollBarVisibility((ScrollViewer)Content, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility((ScrollViewer)Content, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        ShowPage1();
    }

    private Control Header(string title, string sub)
    {
        var stack = new StackPanel { Margin = new Thickness(4, 2, 4, 14) };
        stack.Children.Add(new TextBlock
        {
            Text = title, FontSize = 21, FontWeight = FontWeight.SemiBold, Foreground = DialogUi.Text,
        });
        stack.Children.Add(new TextBlock
        {
            Text = sub, FontSize = 12, Foreground = DialogUi.Muted,
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });
        return stack;
    }

    private void ShowPage1()
    {
        _pageHost.Children.Clear();
        _pageHost.Children.Add(Header(
            _settings.WasCreated ? "Welcome to Winotch" : "Rerun setup",
            "Pick how the island should look — you can change all of this later from Settings."));
        _pageHost.Children.Add(StepDots(0));
        _pageHost.Children.Add(BuildPreview());

        _pageHost.Children.Add(Card(LabeledChips("Theme", new[]
        {
            (ThemeMode.Dark, "Dark"), (ThemeMode.Light, "Light"), (ThemeMode.System, "System"),
            (ThemeMode.Color, "Colour"), (ThemeMode.Background, "Background"),
        }, _theme, value => { _theme = value; RefreshPreview(); })));
        _pageHost.Children.Add(Card(LabeledChips("Type", new[]
        {
            (NotchStyle.Notch, "Notch"), (NotchStyle.Island, "Island"),
        }, _style, value => { _style = value; RefreshPreview(); })));
        _pageHost.Children.Add(Card(LabeledChips("Surface", new[]
        {
            (true, "Glass"), (false, "Solid"),
        }, _glass, value => { _glass = value; RefreshPreview(); })));
        _pageHost.Children.Add(Card(LabeledChips("Open direction", new[]
        {
            (ExpandDirection.Vertical, "Vertical"), (ExpandDirection.Horizontal, "Horizontal"),
        }, _openDirection, value => { _openDirection = value; RefreshPreview(); })));

        var next = DialogUi.Button("Next", primary: true);
        next.Click += (_, _) => ShowPage2();
        var hint = new TextBlock
        {
            Text = "Live preview above — tap the chips to see it change",
            FontSize = 10.5, Foreground = DialogUi.Muted,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _pageHost.Children.Add(FooterRow(null, next, hint));
    }

    private Border BuildPreview()
    {
        var led = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var clock = new TextBlock
        {
            Text = DateTime.Now.ToString("h:mm"), FontSize = 15, FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _previewPill = new Border
        {
            Width = 220, Height = 45, CornerRadius = new CornerRadius(22),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children = { led, clock },
            },
        };
        _previewCaption = new TextBlock
        {
            FontSize = 10.5, Foreground = DialogUi.Muted,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var stack = new StackPanel { Children = { _previewPill, _previewCaption } };
        _previewHost = new Border { Child = stack, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 14) };
        RefreshPreview();
        return _previewHost;
    }

    private void RefreshPreview()
    {
        if (_previewPill is null) return;
        var light = _theme == ThemeMode.Light;
        _previewPill.Background = new SolidColorBrush(
            _glass
                ? Color.FromRgb(light ? (byte)0xEA : (byte)0x23, light ? (byte)0xEE : (byte)0x27, light ? (byte)0xF5 : (byte)0x2F)
                : Color.FromRgb(light ? (byte)0xF3 : (byte)0x1A, light ? (byte)0xF5 : (byte)0x1D, light ? (byte)0xF9 : (byte)0x24));
        _previewPill.BorderBrush = new SolidColorBrush(
            Color.FromRgb(light ? (byte)0xC9 : (byte)0x2E, light ? (byte)0xD1 : (byte)0x34, light ? (byte)0xDD : (byte)0x40));
        if (_previewPill.Child is StackPanel row && row.Children.Count == 2)
        {
            if (row.Children[0] is Ellipse led)
            {
                led.Fill = new SolidColorBrush(light ? Color.FromRgb(0x00, 0x78, 0xD4) : Color.FromRgb(0x50, 0xD6, 0xFF));
            }
            if (row.Children[1] is TextBlock clock)
            {
                clock.Foreground = new SolidColorBrush(light ? Color.FromRgb(0x14, 0x19, 0x22) : Color.FromRgb(0xF2, 0xF4, 0xF8));
            }
        }
        _previewCaption.Text = _style == NotchStyle.Notch
            ? "Attaches to the top edge — the cap tucks under the screen"
            : "Floats below the top edge";
        _previewCaption.Text += _openDirection == ExpandDirection.Horizontal
            ? " · widgets in two columns"
            : " · opens downward";
    }

    private static Control StepDots(int active)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14),
        };
        for (var i = 0; i < 2; i++)
        {
            panel.Children.Add(new Ellipse
            {
                Width = 7, Height = 7,
                Fill = i == active ? DialogUi.Accent : DialogUi.CardHover,
                Margin = new Thickness(4),
            });
        }
        return panel;
    }

    private static Border Card(Control inner)
    {
        return new Border
        {
            Background = DialogUi.Card, BorderBrush = DialogUi.Stroke,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(12, 10, 12, 4), Margin = new Thickness(0, 0, 0, 12),
            Child = inner,
        };
    }

    private void ShowPage2()
    {
        _pageHost.Children.Clear();
        _pageHost.Children.Add(Header(
            "Where it lives & what it shows",
            "One island per screen is supported — pick a display, then arrange your widgets."));
        _pageHost.Children.Add(StepDots(1));

        _pageHost.Children.Add(Card(LabeledChips("Display", new[]
        {
            (DisplayMode.Main, "Main"), (DisplayMode.All, "All monitors"),
            (DisplayMode.FollowActive, "Follow active"), (DisplayMode.Named, "Specific…"),
        }, _display, value =>
        {
            _display = value;
            if (_displayMapCard is not null)
                _displayMapCard.IsVisible = value == DisplayMode.Named;
        })));

        _displayMapCard = BuildDisplayMap();
        _displayMapCard.IsVisible = _display == DisplayMode.Named;
        _pageHost.Children.Add(_displayMapCard);

        _launcherCard = BuildLauncherCard();
        _pageHost.Children.Add(BuildWidgetsCard());
        _pageHost.Children.Add(_launcherCard);
        UpdateLauncherCardVisibility();

        var back = DialogUi.Button("Back");
        back.Click += (_, _) => ShowPage1();
        var done = DialogUi.Button("Get started", primary: true);
        done.Click += (_, _) => Apply();

        _pageHost.Children.Add(FooterRow(back, done));
    }

    private static Border FooterRow(Button? back, Button primary, Control? hint = null)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        if (hint is not null) row.Children.Add(hint);
        if (back is not null)
        {
            back.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(back);
        }
        row.Children.Add(primary);
        return new Border { Child = row, Margin = new Thickness(4, 0, 4, 0) };
    }

    private static Control LabeledChips<T>(string label, IReadOnlyList<(T Value, string Label)> options,
        T current, Action<T> onChanged)
    {
        var text = new TextBlock
        {
            Text = label.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeight.SemiBold,
            Foreground = DialogUi.Muted, FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            Margin = new Thickness(4, 0, 0, 6),
        };
        var wrap = new WrapPanel { Margin = new Thickness(4, 0, 4, 14) };
        var chips = new List<Border>();
        var labels = new List<TextBlock>();

        void Paint()
        {
            for (var i = 0; i < chips.Count; i++)
            {
                var selected = Equals(options[i].Value, current);
                chips[i].Background = selected ? DialogUi.Accent : DialogUi.CardHover;
                chips[i].BorderBrush = selected ? DialogUi.Accent : DialogUi.Stroke;
                chips[i].BorderThickness = new Thickness(1);
                labels[i].Foreground = selected ? DialogUi.OnAccent : DialogUi.Text;
            }
        }

        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            var labelText = new TextBlock
            {
                Text = option.Label, FontSize = 12, FontWeight = FontWeight.SemiBold,
            };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(13, 5, 13, 5),
                Margin = new Thickness(0, 0, 8, 8),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = labelText,
            };
            chip.PointerPressed += (_, _) =>
            {
                current = option.Value;
                onChanged(option.Value);
                Paint();
            };
            chips.Add(chip);
            labels.Add(labelText);
            wrap.Children.Add(chip);
        }
        Paint();

        return new StackPanel { Children = { text, wrap } };
    }

    private Border BuildLauncherCard()
    {
        var text = new TextBlock
        {
            Text = "LAUNCHER APPS", FontSize = 10, FontWeight = FontWeight.SemiBold,
            Foreground = DialogUi.Muted, FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            Margin = new Thickness(4, 0, 0, 6),
        };
        _launcherEmpty = new TextBlock
        {
            Text = "Scanning installed apps\u2026", FontSize = 11.5, Foreground = DialogUi.Muted,
            Margin = new Thickness(4, 0, 4, 8),
        };
        _launcherChips = new WrapPanel { Margin = new Thickness(4, 0, 4, 0) };
        var stack = new StackPanel { Children = { text, _launcherEmpty, _launcherChips } };
        return new Border
        {
            Child = stack, Background = DialogUi.Panel, BorderBrush = DialogUi.Stroke,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 10, 10, 10), Margin = new Thickness(4, 12, 4, 0),
        };
    }

    private void UpdateLauncherCardVisibility()
    {
        if (_launcherCard is { } card)
        {
            card.IsVisible = _widgets.Contains("launcher");
        }
        if (_widgets.Contains("launcher") && !_launcherScanStarted)
        {
            _launcherScanStarted = true;
            StartLauncherScan();
        }
    }

    private void StartLauncherScan()
    {
        _ = Task.Run(() =>
        {
            List<LauncherApp> found;
            try
            {
                // Use the launcher service from platform layer
                var svc = new Platform.Linux.LinuxLauncherService();
                found = svc.ScanApplications()
                    .Select(a => new LauncherApp(a.Name, a.ExecutablePath))
                    .ToList();
            }
            catch
            {
                found = new List<LauncherApp>();
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (_launcherEmpty is { } empty)
                {
                    empty.Text = found.Count == 0
                        ? "No installed apps found \u2014 add shortcuts from the widget's \u2699 settings later."
                        : "Tap to include or exclude:";
                }
                PaintLauncherChips(found);
            });
        });
    }

    private void PaintLauncherChips(List<LauncherApp> found)
    {
        if (_launcherChips is null) return;
        _launcherChips.Children.Clear();
        foreach (var app in found)
        {
            var selected = _launcherApps.Any(a =>
                string.Equals(a.Path, app.Path, StringComparison.OrdinalIgnoreCase));
            var chipLabel = new TextBlock
            {
                Text = (selected ? "\u2713 " : "+ ") + app.Label,
                FontSize = 11.5, FontWeight = FontWeight.SemiBold,
            };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = selected ? DialogUi.Accent : DialogUi.CardHover,
                BorderBrush = selected ? DialogUi.Accent : DialogUi.Stroke,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = chipLabel,
            };
            chip.PointerPressed += (_, _) =>
            {
                var already = _launcherApps.Any(a =>
                    string.Equals(a.Path, app.Path, StringComparison.OrdinalIgnoreCase));
                if (already)
                    _launcherApps.RemoveAll(a => string.Equals(a.Path, app.Path, StringComparison.OrdinalIgnoreCase));
                else
                    _launcherApps.Add(app);
                chipLabel.Text = (already ? "+ " : "\u2713 ") + app.Label;
                chip.Background = already ? DialogUi.CardHover : DialogUi.Accent;
                chip.BorderBrush = already ? DialogUi.Stroke : DialogUi.Accent;
            };
            _launcherChips.Children.Add(chip);
        }
    }

    private Border BuildDisplayMap()
    {
        var monitors = MonitorTargeting.CaptureScreens();
        var map = new Grid
        {
            Background = DialogUi.Card,
            Margin = new Thickness(4, 0, 4, 14),
            Height = 150,
        };
        var label = new TextBlock
        {
            Text = "Pick a display on the map", FontSize = 11.5, Foreground = DialogUi.Muted,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        map.Children.Add(label);

        if (monitors.Count > 0)
        {
            var minX = monitors.Min(m => m.Bounds.Left);
            var minY = monitors.Min(m => m.Bounds.Top);
            var maxRight = monitors.Max(m => m.Bounds.Right);
            var maxBottom = monitors.Max(m => m.Bounds.Bottom);
            var scale = Math.Min(380.0 / Math.Max(1, maxRight - minX), 110.0 / Math.Max(1, maxBottom - minY));

            foreach (var monitor in monitors)
            {
                var width = Math.Max(30, monitor.Bounds.Width * scale);
                var height = Math.Max(20, monitor.Bounds.Height * scale);
                var x = (monitor.Bounds.Left - minX) * scale;
                var y = (monitor.Bounds.Top - minY) * scale;
                var name = monitor.IsPrimary ? "Main" : monitor.DeviceName.TrimStart('\\').Replace("DISPLAY", "Screen ");
                var text = new TextBlock
                {
                    Text = $"{name}\n{monitor.Bounds.Width}×{monitor.Bounds.Height}",
                    FontSize = 9.5, TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = DialogUi.Text,
                };
                var button = new Border
                {
                    Width = width, Height = height, Background = DialogUi.CardHover,
                    BorderBrush = DialogUi.Stroke, BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6), Child = text,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Margin = new Thickness(x, y, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                };
                var deviceName = monitor.DeviceName;
                button.PointerPressed += (_, _) =>
                {
                    _display = DisplayMode.Named;
                    _displayDevice = deviceName;
                    foreach (var other in map.Children.OfType<Border>())
                    {
                        other.BorderBrush = DialogUi.Stroke;
                        other.BorderThickness = new Thickness(1);
                    }
                    button.BorderBrush = DialogUi.Accent;
                    button.BorderThickness = new Thickness(2);
                };
                if (string.Equals(_displayDevice, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    button.BorderBrush = DialogUi.Accent;
                    button.BorderThickness = new Thickness(2);
                }
                map.Children.Add(button);
            }
        }

        return new Border { Child = map };
    }

    private Control BuildWidgetsCard()
    {
        var text = new TextBlock
        {
            Text = "WIDGETS", FontSize = 10, FontWeight = FontWeight.SemiBold,
            Foreground = DialogUi.Muted, FontFamily = new FontFamily("Consolas, Courier New, monospace"),
            Margin = new Thickness(4, 0, 0, 6),
        };
        var list = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
        var addWrap = new WrapPanel { Margin = new Thickness(4, 6, 4, 0) };

        void Paint()
        {
            list.Children.Clear();
            foreach (var id in _widgets)
            {
                var labelText = new TextBlock
                {
                    Text = WidgetSettings.Labels[id], FontSize = 12,
                    FontWeight = FontWeight.SemiBold, Foreground = DialogUi.Text,
                };
                var remove = new TextBlock
                {
                    Text = "\u2715", FontSize = 11, Foreground = DialogUi.Muted,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0),
                };
                var row = new Border
                {
                    Background = DialogUi.Card, CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 6, 8, 6), Margin = new Thickness(0, 0, 0, 6),
                    Child = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Children = { labelText, remove },
                    },
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                var widgetId = id;
                row.PointerPressed += (_, _) =>
                {
                    _widgets.Remove(widgetId);
                    Paint();
                };
                list.Children.Add(row);
                UpdateLauncherCardVisibility();
            }

            addWrap.Children.Clear();
            foreach (var id in WidgetSettings.Known)
            {
                if (_widgets.Contains(id)) continue;
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = DialogUi.CardHover, BorderBrush = DialogUi.Stroke,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6),
                    Child = new TextBlock { Text = "+ " + WidgetSettings.Labels[id], FontSize = 11.5, Foreground = DialogUi.Text },
                    Cursor = new Cursor(StandardCursorType.Hand),
                };
                var widgetId = id;
                chip.PointerPressed += (_, _) =>
                {
                    _widgets.Add(widgetId);
                    Paint();
                };
                addWrap.Children.Add(chip);
                UpdateLauncherCardVisibility();
            }
        }
        Paint();

        var scroll = new ScrollViewer
        {
            Content = list, MaxHeight = 170,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(scroll, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);

        var stack = new StackPanel { Children = { text, scroll, addWrap } };
        return new Border
        {
            Child = stack, Background = DialogUi.Panel,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 10, 10, 8), Margin = new Thickness(4, 0, 4, 0),
        };
    }

    private void Apply()
    {
        if (_widgets.Count == 0) _widgets = new List<string>(WidgetSettings.DefaultEnabled);
        _settings.Update(s => s with
        {
            General = s.General with { Style = _style, OpenDirection = _openDirection },
            Appearance = s.Appearance with
            {
                Theme = _theme, Glass = _glass,
                Display = _display, DisplayDeviceName = _displayDevice,
            },
            Widgets = s.Widgets with
            {
                Enabled = _widgets,
                LauncherApps = _launcherApps.Count > 0 ? _launcherApps : s.Widgets.LauncherApps,
            },
        });
        Close(true);
    }
}
