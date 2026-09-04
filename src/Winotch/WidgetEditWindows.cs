using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Winotch.Engine;

namespace Winotch;

/// <summary>Shared styling for the small widget-edit dialogs. The palette is
/// resolved from the current shell theme at dialog-open time so the light
/// theme (and custom accents) reach every surface, like the legacy app.</summary>
internal static class DialogUi
{
    /// <summary>The theme new dialogs should be built with; set by the host
    /// whenever the theme changes.</summary>
    public static ShellTheme Current { get; set; } = ShellTheme.Dark;

    public static SolidColorBrush Panel => Brush(Current.PanelTop);
    public static SolidColorBrush Card => Brush(Current.Card);
    public static SolidColorBrush CardHover => Brush(Current.CardHover);
    public static SolidColorBrush Stroke => Brush(Current.Stroke);
    public static SolidColorBrush Text => Brush(Current.Text);
    public static SolidColorBrush Muted => Brush(Current.Muted);
    public static SolidColorBrush Accent => Brush(Current.Accent);
    public static SolidColorBrush Danger => Brush(Current.Danger);
    public static SolidColorBrush OnAccent => Brush(Current.IsLight ? Col.White : new Col(0x06, 0x13, 0x1A));

    private static SolidColorBrush Brush(Col c) => new(Color.FromArgb(c.A, c.R, c.G, c.B));

    public static SolidColorBrush B(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    public static TextBox TextBox(string text = "", bool multiline = false, string placeholder = "")
    {
        var box = new TextBox
        {
            Text = text,
            Background = Card,
            Foreground = Text,
            BorderBrush = Stroke,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 12.5,
            CaretBrush = Text,
            SelectionBrush = Accent,
        };
        if (multiline)
        {
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            box.MinHeight = 120;
        }
        if (placeholder.Length > 0)
        {
            var watermarked = new WatermarkTextBox
            {
                Text = box.Text,
                Background = box.Background,
                Foreground = box.Foreground,
                BorderBrush = box.BorderBrush,
                BorderThickness = box.BorderThickness,
                Padding = box.Padding,
                FontSize = box.FontSize,
                CaretBrush = box.CaretBrush,
                SelectionBrush = box.SelectionBrush,
                AcceptsReturn = box.AcceptsReturn,
                TextWrapping = box.TextWrapping,
                VerticalScrollBarVisibility = box.VerticalScrollBarVisibility,
                MinHeight = box.MinHeight,
                Placeholder = placeholder,
            };
            return watermarked;
        }
        return box;
    }

    public static Button Button(string content, bool primary = false, bool danger = false)
    {
        var b = new Button
        {
            Content = content,
            Background = primary ? Accent : CardHover,
            Foreground = primary ? B(0x06, 0x13, 0x1A) : Text,
            BorderBrush = primary ? Accent : Stroke,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(14, 6, 14, 6),
            FontSize = 12,
            FontWeight = primary || danger ? FontWeights.SemiBold : FontWeights.Normal,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        if (danger) b.Foreground = Danger;
        return b;
    }

    public static TextBlock Label(string text, double size = 12.5, bool muted = false, FontWeight? weight = null)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            Foreground = muted ? Muted : Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (weight is not null) tb.FontWeight = weight.Value;
        return tb;
    }

    /// <summary>Compact / Standard / Large segmented picker for one widget's
    /// card size. The selected segment stays accent-highlighted and the new
    /// choice is reported through <paramref name="changed"/> as it's made.</summary>
    public static StackPanel SizeRow(WidgetScale current, Action<WidgetScale> changed)
    {
        var options = new[] { WidgetScale.Compact, WidgetScale.Standard, WidgetScale.Large };
        var buttons = new List<Button>();
        void Paint(WidgetScale selected)
        {
            for (var i = 0; i < buttons.Count; i++)
            {
                var on = options[i] == selected;
                buttons[i].Background = on ? Accent : CardHover;
                buttons[i].Foreground = on ? B(0x06, 0x13, 0x1A) : Text;
            }
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        for (var i = 0; i < options.Length; i++)
        {
            var scale = options[i];
            var button = Button(scale.Label());
            button.Padding = new Thickness(0, 5, 0, 5);
            button.MinWidth = 76;
            button.Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0);
            button.Click += (_, _) =>
            {
                Paint(scale);
                changed(scale);
            };
            buttons.Add(button);
            row.Children.Add(button);
        }
        Paint(current);
        return row;
    }

    /// <summary>Labeled card-size picker to drop into a widget-edit dialog.</summary>
    public static StackPanel SizeSection(WidgetScale current, Action<WidgetScale> changed)
    {
        var label = Label("Card size", 11.5, muted: true);
        label.Margin = new Thickness(0, 12, 0, 0);
        return new StackPanel { Children = { label, SizeRow(current, changed) } };
    }

    public static void ApplyFrame(Window window, string title)
    {
        window.Title = title;
        window.Background = Panel;
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Topmost = true;
        window.SizeToContent = SizeToContent.WidthAndHeight;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.Icon = AppIcon.Load();
        // Follow the Appearance font choice; fall back to Segoe UI when the
        // shared resource has not been populated yet.
        window.FontFamily = (Application.Current.Resources["NotchUiFont"] as FontFamily)
            ?? new FontFamily("Segoe UI");
    }
}

/// <summary>Loads the Winotch tray icon as a window/taskbar icon. The icon
/// file ships with multi-size PNG frames, so the title bar gets a crisp
/// small frame while Alt-Tab gets the large one (item: the logo looked
/// broken because the old .ico only had 16/24 px frames).</summary>
public static class AppIcon
{
    public static System.Windows.Media.ImageSource? Load()
    {
        try
        {
            var resource = Application.GetResourceStream(
                new Uri("pack://application:,,,/Winotch;component/Resources/WinotchTray.ico"));
            if (resource?.Stream is null) return null;
            using var stream = resource.Stream;
            var decoder = new System.Windows.Media.Imaging.IconBitmapDecoder(stream,
                System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            return decoder.Frames.Count > 0 ? decoder.Frames[0] : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>A TextBox that actually draws its placeholder while empty, in the
/// muted foreground so it reads as a hint (the old attached property only
/// recolored the foreground and never showed the hint text).</summary>
public sealed class WatermarkTextBox : TextBox
{
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(WatermarkTextBox),
        new PropertyMetadata(string.Empty, OnPlaceholderChanged));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    private static void OnPlaceholderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WatermarkTextBox box) box.InvalidateVisual();
    }

    protected override void OnTextChanged(TextChangedEventArgs e)
    {
        base.OnTextChanged(e);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (Text.Length != 0 || string.IsNullOrEmpty(Placeholder)) return;
        var formatted = new FormattedText(
            Placeholder,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize,
            DialogUi.Muted,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var x = Padding.Left + BorderThickness.Left + 1;
        var y = Padding.Top + BorderThickness.Top + 1;
        dc.DrawText(formatted, new Point(x, y));
    }
}

/// <summary>Multiline notes editor with a live character count.</summary>
public sealed class NotesEditWindow : Window
{
    public string? Result { get; private set; }

    /// <summary>The card size chosen in this dialog.</summary>
    public WidgetScale Scale { get; private set; }

    public NotesEditWindow(string current, WidgetScale scale)
    {
        DialogUi.ApplyFrame(this, "Notes");
        Scale = scale;
        var box = DialogUi.TextBox(current, multiline: true);
        var count = DialogUi.Label($"{current.Length} chars", 11, muted: true);

        var ok = DialogUi.Button("Save", primary: true);
        var cancel = DialogUi.Button("Cancel");
        ok.Click += (_, _) => { Result = box.Text; DialogResult = true; };
        cancel.Click += (_, _) => DialogResult = false;
        box.TextChanged += (_, _) => count.Text = $"{box.Text.Length} chars";
        box.Loaded += (_, _) => { box.Focus(); box.CaretIndex = box.Text.Length; };

        var panel = new StackPanel { Margin = new Thickness(16), Width = 340 };
        panel.Children.Add(DialogUi.Label("Jot something down…", 12, muted: true));
        panel.Children.Add(box);
        panel.Children.Add(count);
        panel.Children.Add(DialogUi.SizeSection(scale, s => Scale = s));
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancel, ok },
        });
        ((StackPanel)panel.Children[^1]).Children[1].SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));

        Content = panel;
    }
}

/// <summary>City entry plus "Use Windows location"; geocoding + fetch happen
/// after the dialog closes so it stays snappy.</summary>
public sealed class WeatherEditWindow : Window
{
    public string? City { get; private set; }
    public bool UseWindowsLocation { get; private set; }

    /// <summary>The card size chosen in this dialog.</summary>
    public WidgetScale Scale { get; private set; }

    public WeatherEditWindow(WeatherWidgetConfig config, WidgetScale scale)
    {
        DialogUi.ApplyFrame(this, "Weather");
        Scale = scale;
        var box = DialogUi.TextBox(config.City, placeholder: "e.g. Berlin");

        var ok = DialogUi.Button("Save", primary: true);
        var cancel = DialogUi.Button("Cancel");
        var useLocation = DialogUi.Button("Use Windows location");
        ok.Click += (_, _) =>
        {
            var city = box.Text.Trim();
            if (city.Length == 0)
            {
                box.Focus();
                return;
            }
            City = city;
            DialogResult = true;
        };
        cancel.Click += (_, _) => DialogResult = false;
        useLocation.Click += (_, _) =>
        {
            UseWindowsLocation = true;
            DialogResult = true;
        };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };

        var panel = new StackPanel { Margin = new Thickness(16), Width = 320 };
        panel.Children.Add(DialogUi.Label("City / location", 12, muted: true));
        panel.Children.Add(box);
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { useLocation },
        });
        panel.Children.Add(DialogUi.SizeSection(scale, s => Scale = s));
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { cancel, ok },
        });
        ((StackPanel)panel.Children[^1]).Children[1].SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));

        Content = panel;
    }
}

/// <summary>Launcher app list editor: remove rows, add name+path, quick-add
/// well-known apps.</summary>
public sealed class LauncherEditWindow : Window
{
    public List<LauncherApp>? Result { get; private set; }

    /// <summary>The card size chosen in this dialog.</summary>
    public WidgetScale Scale { get; private set; }

    private static readonly (string Label, string Path)[] QuickApps =
    {
        ("Chrome", "chrome.exe"),
        ("Edge", "msedge.exe"),
        ("Terminal", "wt.exe"),
        ("VS Code", "code.exe"),
        ("Notepad", "notepad.exe"),
        ("Calculator", "calc.exe"),
        ("Paint", "mspaint.exe"),
        ("Spotify", "%APPDATA%\\Spotify\\Spotify.exe"),
        ("Steam", "C:\\Program Files (x86)\\Steam\\steam.exe"),
    };

    public LauncherEditWindow(IReadOnlyList<LauncherApp> current, WidgetScale scale)
    {
        var apps = new List<LauncherApp>(current);
        DialogUi.ApplyFrame(this, "App launcher");
        Scale = scale;
        var width = 380;

        var ok = DialogUi.Button("Done", primary: true);
        var cancel = DialogUi.Button("Cancel");
        ok.Click += (_, _) => { Result = apps; DialogResult = true; };
        cancel.Click += (_, _) => DialogResult = false;

        var list = new StackPanel();
        void Repaint()
        {
            list.Children.Clear();
            foreach (var app in apps)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var label = DialogUi.Label(app.Label, 12, weight: FontWeights.SemiBold);
                var path = DialogUi.Label(app.Path, 10.5, muted: true);
                var stack = new StackPanel { Children = { label, path } };
                Grid.SetColumn(stack, 0);
                var remove = DialogUi.Button("✕", danger: true);
                remove.Padding = new Thickness(8, 3, 8, 3);
                Grid.SetColumn(remove, 1);
                remove.Click += (_, _) => { apps.Remove(app); Repaint(); };
                row.Children.Add(stack);
                row.Children.Add(remove);
                list.Children.Add(row);
            }
            if (apps.Count == 0)
            {
                list.Children.Add(DialogUi.Label("No apps yet — add one below.", 11.5, muted: true));
            }
        }
        Repaint();

        var nameBox = DialogUi.TextBox(placeholder: "Name (optional)");
        nameBox.Width = 100;
        var pathBox = DialogUi.TextBox(placeholder: "File path or name, e.g. chrome.exe");
        var add = DialogUi.Button("Add", primary: true);
        void DoAdd()
        {
            var path = pathBox.Text.Trim();
            if (path.Length == 0) return;
            var name = nameBox.Text.Trim();
            if (name.Length == 0) name = path.Split('\\', '/')[^1];
            if (!apps.Any(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                apps.Add(new LauncherApp(name, path));
            }
            nameBox.Text = "";
            pathBox.Text = "";
            Repaint();
            pathBox.Focus();
        }
        add.Click += (_, _) => DoAdd();
        pathBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) DoAdd();
        };

        var addRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
            Children = { nameBox, pathBox, add },
        };
        ((FrameworkElement)addRow.Children[0]).Margin = new Thickness(0, 0, 6, 0);
        ((FrameworkElement)addRow.Children[1]).Margin = new Thickness(0, 0, 6, 0);

        var quick = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (label, path) in QuickApps)
        {
            if (apps.Any(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
            var chip = DialogUi.Button("+ " + label);
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.Padding = new Thickness(10, 4, 10, 4);
            chip.Click += (_, _) =>
            {
                apps.Add(new LauncherApp(label, path));
                quick.Children.Remove(chip);
                Repaint();
            };
            quick.Children.Add(chip);
        }

        var scroller = new ScrollViewer
        {
            Content = list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 220,
            Margin = new Thickness(0, 4, 0, 0),
        };

        var panel = new StackPanel { Margin = new Thickness(16), Width = width };
        panel.Children.Add(DialogUi.Label("App shortcuts", 12, muted: true));
        panel.Children.Add(scroller);
        panel.Children.Add(addRow);
        panel.Children.Add(quick);
        panel.Children.Add(DialogUi.SizeSection(scale, s => Scale = s));
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { cancel, ok },
        });
        ((StackPanel)panel.Children[^1]).Children[1].SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));

        Content = panel;
    }
}

/// <summary>Checkbox row for the System stats card metrics.</summary>
public sealed class MetricsEditWindow : Window
{
    public SystemMetricsConfig? Result { get; private set; }

    /// <summary>The card size chosen in this dialog.</summary>
    public WidgetScale Scale { get; private set; }

    public MetricsEditWindow(SystemMetricsConfig current, WidgetScale scale)
    {
        DialogUi.ApplyFrame(this, "System metrics");
        Scale = scale;

        var cpu = new CheckBox { Content = "CPU load", IsChecked = current.Cpu, Foreground = DialogUi.Text, Margin = new Thickness(0, 6, 0, 0) };
        var mem = new CheckBox { Content = "Memory", IsChecked = current.Memory, Foreground = DialogUi.Text, Margin = new Thickness(0, 6, 0, 0) };
        var bat = new CheckBox { Content = "Battery", IsChecked = current.Battery, Foreground = DialogUi.Text, Margin = new Thickness(0, 6, 0, 0) };
        var net = new CheckBox { Content = "Network", IsChecked = current.Network, Foreground = DialogUi.Text, Margin = new Thickness(0, 6, 0, 0) };

        var ok = DialogUi.Button("Save", primary: true);
        var cancel = DialogUi.Button("Cancel");
        ok.Click += (_, _) =>
        {
            Result = new SystemMetricsConfig
            {
                Cpu = cpu.IsChecked == true,
                Memory = mem.IsChecked == true,
                Battery = bat.IsChecked == true,
                Network = net.IsChecked == true,
            };
            DialogResult = true;
        };
        cancel.Click += (_, _) => DialogResult = false;

        var panel = new StackPanel { Margin = new Thickness(16), Width = 260 };
        panel.Children.Add(DialogUi.Label("Which metrics to show", 12, muted: true));
        panel.Children.Add(cpu);
        panel.Children.Add(mem);
        panel.Children.Add(bat);
        panel.Children.Add(net);
        panel.Children.Add(DialogUi.SizeSection(scale, s => Scale = s));
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { cancel, ok },
        });
        ((StackPanel)panel.Children[^1]).Children[1].SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));

        Content = panel;
    }
}
/// <summary>Custom focus-timer durations: focus and break minutes plus the
/// auto-cycle toggle (item: let users add their own ratios / timers).</summary>
public sealed class FocusEditWindow : Window
{
    public (int FocusMinutes, int BreakMinutes, bool AutoCycle)? Result { get; private set; }

    /// <summary>The card size chosen in this dialog.</summary>
    public WidgetScale Scale { get; private set; }

    public FocusEditWindow(int focusMinutes, int breakMinutes, bool autoCycle, WidgetScale scale)
    {
        DialogUi.ApplyFrame(this, "Focus timer");
        Scale = scale;

        var focusBox = DialogUi.TextBox(focusMinutes.ToString());
        focusBox.Width = 72;
        var breakBox = DialogUi.TextBox(breakMinutes.ToString());
        breakBox.Width = 72;

        var loop = new CheckBox
        {
            Content = "Auto-cycle (focus → break → focus…)",
            IsChecked = autoCycle,
            Foreground = DialogUi.Text,
            Margin = new Thickness(0, 8, 0, 0),
        };

        var ok = DialogUi.Button("Save", primary: true);
        var cancel = DialogUi.Button("Cancel");
        ok.Click += (_, _) =>
        {
            if (!int.TryParse(focusBox.Text.Trim(), out var focus) ||
                !int.TryParse(breakBox.Text.Trim(), out var breakMin))
            {
                focusBox.Focus();
                return;
            }
            Result = (Math.Clamp(focus, 1, 180), Math.Clamp(breakMin, 1, 60), loop.IsChecked == true);
            DialogResult = true;
        };
        cancel.Click += (_, _) => DialogResult = false;
        focusBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        breakBox.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) ok.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        focusBox.Loaded += (_, _) => { focusBox.Focus(); focusBox.SelectAll(); };

        var focusCol = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        focusCol.Children.Add(DialogUi.Label("Focus (min)", 11, muted: true));
        focusCol.Children.Add(focusBox);
        var breakCol = new StackPanel();
        breakCol.Children.Add(DialogUi.Label("Break (min)", 11, muted: true));
        breakCol.Children.Add(breakBox);

        var panel = new StackPanel { Margin = new Thickness(16), Width = 300 };
        panel.Children.Add(DialogUi.Label("Custom session", 12, muted: true));
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 4, 0, 0),
            Children = { focusCol, breakCol },
        });
        panel.Children.Add(loop);
        panel.Children.Add(DialogUi.SizeSection(scale, s => Scale = s));
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { cancel, ok },
        });
        ((StackPanel)panel.Children[^1]).Children[1].SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));

        Content = panel;
    }
}

/// <summary>Settings dialog for cards with nothing else to configure (clock,
/// now playing, clipboard): just the card size picker.</summary>
public sealed class CardSizeWindow : Window
{
    /// <summary>The card size chosen in this dialog.</summary>
    public WidgetScale Scale { get; private set; }

    public CardSizeWindow(string title, WidgetScale current)
    {
        DialogUi.ApplyFrame(this, title);
        Scale = current;

        var ok = DialogUi.Button("Done", primary: true);
        var cancel = DialogUi.Button("Cancel");
        ok.Click += (_, _) => DialogResult = true;
        cancel.Click += (_, _) => DialogResult = false;

        var panel = new StackPanel { Margin = new Thickness(16), Width = 290 };
        panel.Children.Add(DialogUi.SizeSection(current, s => Scale = s));
        panel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { cancel, ok },
        });
        ((StackPanel)panel.Children[^1]).Children[1].SetValue(FrameworkElement.MarginProperty, new Thickness(8, 0, 0, 0));

        Content = panel;
    }
}
