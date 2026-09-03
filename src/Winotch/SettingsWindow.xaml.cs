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
        BuildRows();
    }

    private void BuildRows()
    {
        var s = _settings.Current;
        Rows.Children.Add(MakeToggle("24-hour clock", s.General.Use24HourClock,
            v => _settings.Update(x => x with { General = x.General with { Use24HourClock = v } })));
        Rows.Children.Add(MakeToggle("Show the date", s.General.ShowDate,
            v => _settings.Update(x => x with { General = x.General with { ShowDate = v } })));
        Rows.Children.Add(MakeToggle("Attach notch to the top edge (unchecked = floating island)",
            s.General.Style == NotchStyle.Notch,
            v => _settings.Update(x => x with { General = x.General with { Style = v ? NotchStyle.Notch : NotchStyle.Island } })));
        Rows.Children.Add(MakeToggle("Start with Windows", s.General.StartWithWindows,
            v =>
            {
                _settings.Update(x => x with { General = x.General with { StartWithWindows = v } });
                var executable = StartupService.CurrentExecutablePath();
                _startup.SetEnabled(v, executable);
            }));
        Rows.Children.Add(Separator());
        Rows.Children.Add(MakeToggle("Media toasts", s.Toasts.MediaToastsEnabled,
            v => _settings.Update(x => x with { Toasts = x.Toasts with { MediaToastsEnabled = v } })));
        Rows.Children.Add(MakeToggle("Priority alerts (battery / Wi-Fi / mic / camera)", s.Features.PriorityAlertsEnabled,
            v => _settings.Update(x => x with { Features = x.Features with { PriorityAlertsEnabled = v } })));
        Rows.Children.Add(Separator());
        Rows.Children.Add(MakeToggle("Clipboard history (in memory only)", s.Features.ClipboardHistoryEnabled,
            v => _settings.Update(x => x with { Features = x.Features with { ClipboardHistoryEnabled = v } })));
        Rows.Children.Add(MakeToggle("Focus timer", s.Features.FocusTimerEnabled,
            v => _settings.Update(x => x with { Features = x.Features with { FocusTimerEnabled = v } })));
    }

    private static Border Separator() =>
        new() { Height = 1, Background = (Brush)Application.Current.FindResource("NotchStroke"), Margin = new Thickness(0, 10, 0, 10) };

    private Border MakeToggle(string label, bool initial, Action<bool> onChanged)
    {
        var border = new Border { Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(4, 2, 4, 2) };
        var check = new CheckBox { Content = label, FontSize = 12.5, IsChecked = initial };
        check.Checked += (_, _) => onChanged(true);
        check.Unchecked += (_, _) => onChanged(false);
        border.Child = check;
        return border;
    }
}
