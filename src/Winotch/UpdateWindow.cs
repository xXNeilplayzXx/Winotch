using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace Winotch;

/// <summary>"Update available" dialog. Confirming downloads the release zip
/// (progress shown inline), stages the new exe, and hands its path to the
/// host, which schedules the swap and quits so the new build can start.</summary>
public sealed class UpdateWindow : Window
{
    private readonly UpdateInfo _info;
    private readonly Button _go;
    private readonly Button _later;
    private readonly ProgressBar _progress;
    private readonly TextBlock _status;

    /// <summary>The staged new exe, set once the download + extraction finish.</summary>
    public string? StagedExe { get; private set; }

    /// <summary>The downloaded zip, removed after the exe swap succeeds.</summary>
    public string? DownloadPath { get; private set; }

    public UpdateWindow(UpdateInfo info)
    {
        _info = info;
        DialogUi.ApplyFrame(this, "Update available");
        Width = 400;
        SizeToContent = SizeToContent.Height;

        var stack = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
        stack.Children.Add(DialogUi.Label("A new version is available", 16, weight: FontWeights.SemiBold));
        stack.Children.Add(new TextBlock
        {
            Text = $"Winotch v{_info.Version} is ready — you're running {UpdateService.CurrentVersionText}.",
            FontSize = 12,
            Foreground = DialogUi.Muted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
        });

        _progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 8,
            Margin = new Thickness(0, 16, 0, 0),
            Background = DialogUi.CardHover,
            Foreground = DialogUi.Accent,
            BorderThickness = new Thickness(0),
            Visibility = Visibility.Collapsed,
        };
        stack.Children.Add(_progress);

        _status = DialogUi.Label("", 11, muted: true);
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(_status);

        _go = DialogUi.Button("Update & restart", primary: true);
        _go.Click += async (_, _) => await RunAsync();
        _later = DialogUi.Button("Later");
        _later.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        buttons.Children.Add(_later);
        _go.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(_go);
        stack.Children.Add(buttons);
        Content = stack;
    }

    private async Task RunAsync()
    {
        _go.IsEnabled = false;
        _later.IsEnabled = false;
        _progress.Value = 0;
        _progress.Visibility = Visibility.Visible;
        _status.Text = "Downloading…";
        _status.Foreground = DialogUi.Muted;

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Winotch", "Updates");
        var zip = Path.Combine(dir, $"{_info.Tag}.zip");
        var stage = Path.Combine(dir, _info.Tag);
        try
        {
            var update = new UpdateService();
            await update.DownloadAsync(_info.ZipUrl, zip, new Progress<int>(p => _progress.Value = p));
            _status.Text = "Preparing…";
            StagedExe = UpdateService.StageExe(zip, stage);
            DownloadPath = zip;
            DialogResult = true;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or InvalidDataException)
        {
            TryDelete(zip);
            _status.Text = "Download failed — check your connection and try again.";
            _status.Foreground = DialogUi.Danger;
            _progress.Visibility = Visibility.Collapsed;
            _go.IsEnabled = true;
            _later.IsEnabled = true;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Best effort — the stale download is harmless.
        }
    }
}
