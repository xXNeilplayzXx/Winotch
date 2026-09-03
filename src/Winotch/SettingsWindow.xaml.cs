using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Winotch;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly StartupService _startup;

    public SettingsWindow(SettingsService settings, StartupService startup)
    {
        _settings = settings;
        _startup = startup;
        InitializeComponent();
        VersionText.Text = AssemblyVersion();
        BuildSections();
    }

    private string AssemblyVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "" : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    private void BuildSections()
    {
        var s = _settings.Current;

        AddSection("General",
            MakeToggleRow("24-hour clock", "Show times in 24h format",
                s.General.Use24HourClock,
                v => _settings.Update(x => x with { General = x.General with { Use24HourClock = v } })),
            MakeToggleRow("Show the date", "Day + month in the pill and clock widget",
                s.General.ShowDate,
                v => _settings.Update(x => x with { General = x.General with { ShowDate = v } })),
            MakeToggleRow("Attach to the top edge", "Unchecked = floating island below the edge",
                s.General.Style == NotchStyle.Notch,
                v => _settings.Update(x => x with { General = x.General with { Style = v ? NotchStyle.Notch : NotchStyle.Island } })),
            MakeToggleRow("Start with Windows", "Launch at sign-in from the tray",
                s.General.StartWithWindows,
                v =>
                {
                    _settings.Update(x => x with { General = x.General with { StartWithWindows = v } });
                    var executable = StartupService.CurrentExecutablePath();
                    _startup.SetEnabled(v, executable);
                }));

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