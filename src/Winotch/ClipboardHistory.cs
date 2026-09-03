using System.IO;

namespace Winotch;

public sealed record ClipboardEntry(long Id, string Kind, string? Text, string[]? Files, string? ThumbBase64, DateTimeOffset At)
{
    public string Preview(int max = 56) => Kind switch
    {
        "text" => (Text ?? "")[..Math.Min(max, (Text ?? "").Length)],
        "files" => string.Join(", ", (Files ?? []).Select(Path.GetFileName)),
        _ => "Image",
    };

    public string FullText => Kind switch
    {
        "text" => Text ?? "",
        "files" => string.Join(", ", Files ?? []),
        _ => "",
    };
}

/// <summary>In-memory clipboard ring buffer (cap 10). The poller lives on the
/// UI thread (WinForms Clipboard requires STA); the store itself is pure and
/// unit-testable. Nothing here ever touches disk.</summary>
public sealed class ClipboardHistoryStore
{
    public const int Capacity = 10;
    private readonly List<ClipboardEntry> _items = [];
    private long _nextId;
    private string _lastFingerprint = "";

    public IReadOnlyList<ClipboardEntry> Items
    {
        get { lock (_items) { return _items.ToArray(); } }
    }

    /// <summary>Add a captured clipboard state; returns true when it was a
    /// genuinely new item (dedupes against the last capture).</summary>
    public bool TryAdd(string kind, string? text, string[]? files, string? thumbBase64, string fingerprint, DateTimeOffset at)
    {
        if (string.Equals(fingerprint, _lastFingerprint, StringComparison.Ordinal)) return false;
        _lastFingerprint = fingerprint;

        if (string.IsNullOrEmpty(text) && (files is null || files.Length == 0) && kind != "image")
        {
            return false;
        }

        lock (_items)
        {
            _items.Insert(0, new ClipboardEntry(++_nextId, kind, text, files, thumbBase64, at));
            if (_items.Count > Capacity) _items.RemoveRange(Capacity, _items.Count - Capacity);
        }
        return true;
    }

    public void Remove(long id)
    {
        lock (_items) { _items.RemoveAll(i => i.Id == id); }
    }

    public void Clear()
    {
        lock (_items) { _items.Clear(); }
    }
}
