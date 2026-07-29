using System.Diagnostics;

namespace SoftwareCrawler;

/// <summary>
/// Round-trips one item's XPaths and scripts through a file an external editor
/// can open. The five slots become one .js file and come back split again.
///
/// Split out of <see cref="MainForm"/>: the separator, the temp file's name, the
/// editor to fall back on and the way text is normalised on the way back are
/// rules about editing a recipe, not about the menu item that starts it. What
/// stays in the window is asking the questions - reload or overwrite, and when
/// the user is done editing.
/// </summary>
public sealed class ScriptEditSession
{
    /// <summary>
    /// What the five slots are joined with. A comment, so the file the editor
    /// opens is still valid JavaScript.
    /// </summary>
    private const string ScriptSeparator = "\n// ``\n";

    private readonly SoftwareItem _item;

    public ScriptEditSession(SoftwareItem item)
    {
        _item = item;
        FilePath = Path.Join(TempDirectory, SanitizeFileName(item.Name) + ".js");
    }

    private static string TempDirectory => Path.Join(Path.GetTempPath(), "SoftwareCrawler");

    /// <summary>The file the editor is pointed at.</summary>
    public string FilePath { get; }

    /// <summary>
    /// True when a file from an earlier session is still there, which means that
    /// session ended without being applied. Only the caller can decide whether
    /// those edits are worth keeping.
    /// </summary>
    public bool HasUnappliedFile => File.Exists(FilePath);

    /// <summary>Writes the item's current slots out, overwriting whatever was there.</summary>
    public async Task WriteAsync()
    {
        Directory.CreateDirectory(TempDirectory);

        var script = string.Join(ScriptSeparator, _item.GetXPathOrScripts());
        // Editors are happier with a trailing newline.
        script += '\n';

        await File.WriteAllTextAsync(FilePath, script);
    }

    /// <summary>
    /// Opens the file in the configured editor, or Notepad when that setting is
    /// empty or points at something that is no longer installed. Null if the
    /// editor would not start.
    /// </summary>
    public Process? StartEditor()
    {
        var editor = Settings.ExternalJavascriptEditor;
        if (editor == "" || !File.Exists(editor))
            editor = "notepad.exe";

        return Process.Start(editor, $"\"{FilePath}\"");
    }

    /// <summary>
    /// Reads the file back into the item and deletes it so the next session
    /// starts clean. False if the file is gone. Saving the list is left to the
    /// caller, which is what keeps this usable without touching the real one.
    /// </summary>
    public async Task<bool> ApplyAsync()
    {
        if (!File.Exists(FilePath))
            return false;

        var script = await File.ReadAllTextAsync(FilePath);
        File.Delete(FilePath);

        _item.SetXPathOrScripts(Split(script));
        return true;
    }

    /// <summary>Drops the file without touching the item.</summary>
    public void Discard()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }

    /// <summary>
    /// Splits an edited file back into slots. Editors disagree about line endings
    /// and about the blank line at the end, and neither difference belongs in the
    /// stored recipe.
    /// </summary>
    internal static List<string> Split(string script) =>
        script.Trim().Replace("\r\n", "\n").Split(ScriptSeparator).ToList();

    /// <summary>Joins slots the way <see cref="WriteAsync"/> does. For tests.</summary>
    internal static string Join(IEnumerable<string> scripts) =>
        string.Join(ScriptSeparator, scripts);

    /// <summary>
    /// Item names carry slashes and colons, which a file name cannot.
    /// </summary>
    internal static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(
            fileName.Select(character => invalidChars.Contains(character) ? '_' : character)
        );
    }
}
