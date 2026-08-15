using System.Text.Json;

namespace SoftwareCrawler;

/// <summary>
/// Persists the server-side identity of every downloaded archive. The metadata
/// is deliberately kept beside the downloaded or extracted files rather than
/// in the shared recipe or machine settings.
/// </summary>
internal static class DownloadMetadataStore
{
    internal const string FileName = ".softwarecrawler-download-metadata.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    internal sealed class Entry
    {
        public string ItemName { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime? LastModified { get; set; }
    }

    private sealed class Document
    {
        public List<Entry> Downloads { get; set; } = [];
    }

    internal static bool TryGet(string directory, string itemName, out Entry entry)
    {
        entry = null!;
        var document = Read(directory);
        var found = document.Downloads.FirstOrDefault(candidate =>
            candidate.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase)
        );
        if (found is null)
            return false;

        entry = found;
        return true;
    }

    internal static void Write(string directory, Entry entry)
    {
        var path = Path.Join(directory, FileName);
        var document = Read(directory);
        var existing = document.Downloads.FindIndex(candidate =>
            candidate.ItemName.Equals(entry.ItemName, StringComparison.OrdinalIgnoreCase)
        );
        if (existing >= 0)
            document.Downloads[existing] = entry;
        else
            document.Downloads.Add(entry);

        var temporaryPath = path + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static Document Read(string directory)
    {
        var path = Path.Join(directory, FileName);
        try
        {
            if (!File.Exists(path))
                return new Document();

            return JsonSerializer.Deserialize<Document>(File.ReadAllText(path)) ?? new Document();
        }
        catch (Exception)
        {
            // A corrupt or hand-edited sidecar must not prevent a fresh download.
            return new Document();
        }
    }
}
