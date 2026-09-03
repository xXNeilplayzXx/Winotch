using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Winotch;

/// <summary>JSON persistence with normalize-on-load, atomic writes and a
/// corrupt-file fallback (moved aside as settings.bad.json).</summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _gate = new();
    private readonly string _path;
    private WinotchSettings _current;

    public SettingsService() : this(DefaultPath) { }

    public SettingsService(string path)
    {
        _path = path;
        WasCreated = !File.Exists(path);
        _current = Load(path);
    }

    /// <summary>True when no settings file existed at construction — the first
    /// run of a fresh install, used to auto-open the setup wizard.</summary>
    public bool WasCreated { get; }

    public event EventHandler<WinotchSettings>? Changed;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Winotch", "settings.json");

    public WinotchSettings Current
    {
        get { lock (_gate) { return _current; } }
    }

    public void Update(Func<WinotchSettings, WinotchSettings> change)
    {
        WinotchSettings updated;
        lock (_gate)
        {
            updated = change(_current).Normalize();
            if (updated == _current) return;
            _current = updated;
            TrySave(updated);
        }
        Changed?.Invoke(this, updated);
    }

    public static string BadPathFor(string path) =>
        Path.Combine(Path.GetDirectoryName(path) ?? "", "settings.bad.json");

    private static WinotchSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return WinotchSettings.Defaults;
            var json = File.ReadAllText(path, Encoding.UTF8);
            return (JsonSerializer.Deserialize<WinotchSettings>(json, JsonOptions)
                    ?? WinotchSettings.Defaults).Normalize();
        }
        catch (JsonException) { TryRenameBadFile(path); return WinotchSettings.Defaults; }
        catch (NotSupportedException) { TryRenameBadFile(path); return WinotchSettings.Defaults; }
        catch (IOException) { return WinotchSettings.Defaults; }
        catch (UnauthorizedAccessException) { return WinotchSettings.Defaults; }
    }

    private void TrySave(WinotchSettings settings)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var tempPath = Path.Combine(directory ?? "",
                $"settings.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(tempPath,
                JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);
            try
            {
                if (File.Exists(_path)) File.Replace(tempPath, _path, null);
                else File.Move(tempPath, _path);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void TryRenameBadFile(string path)
    {
        try { File.Move(path, BadPathFor(path), overwrite: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
