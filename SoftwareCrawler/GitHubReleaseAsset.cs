using System.Text.Json;

namespace SoftwareCrawler;

/// <summary>
/// A file named by a GitHub release URL:
/// <c>https://github.com/{owner}/{repo}/releases/latest/download/{file}</c> or
/// <c>https://github.com/{owner}/{repo}/releases/download/{tag}/{file}</c>.
/// </summary>
/// <param name="Owner">Repository owner.</param>
/// <param name="Repo">Repository name.</param>
/// <param name="Tag">Release tag, or null when the URL asks for <c>latest</c>.</param>
/// <param name="FileName">Asset file name.</param>
internal readonly record struct GitHubReleaseAsset(
    string Owner,
    string Repo,
    string? Tag,
    string FileName
)
{
    /// <summary>
    /// Reads a release-asset URL. Other GitHub pages, and every other host, are
    /// left alone: this is only the shape whose download gateway answers 504
    /// once a release carries enough assets.
    /// </summary>
    internal static bool TryParse(string? url, out GitHubReleaseAsset asset)
    {
        asset = default;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;
        return TryParse(uri, out asset);
    }

    internal static bool TryParse(Uri uri, out GitHubReleaseAsset asset)
    {
        asset = default;
        if (!uri.IsAbsoluteUri || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6 || !parts[2].Equals("releases", StringComparison.OrdinalIgnoreCase))
            return false;

        string? tag;
        if (
            parts[3].Equals("latest", StringComparison.OrdinalIgnoreCase)
            && parts[4].Equals("download", StringComparison.OrdinalIgnoreCase)
        )
        {
            tag = null;
        }
        else if (parts[3].Equals("download", StringComparison.OrdinalIgnoreCase))
        {
            tag = Uri.UnescapeDataString(parts[4]);
            if (tag.Length == 0)
                return false;
        }
        else
        {
            return false;
        }

        var fileName = Uri.UnescapeDataString(parts[5]);
        if (fileName.Length == 0)
            return false;

        asset = new GitHubReleaseAsset(
            Uri.UnescapeDataString(parts[0]),
            Uri.UnescapeDataString(parts[1]),
            tag,
            fileName
        );
        return true;
    }

    /// <summary>The release JSON that lists this asset. <c>Tag</c> null means latest.</summary>
    internal static string ReleaseApiUrl(GitHubReleaseAsset asset)
    {
        var owner = Uri.EscapeDataString(asset.Owner);
        var repo = Uri.EscapeDataString(asset.Repo);
        if (asset.Tag is null)
            return $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

        return $"https://api.github.com/repos/{owner}/{repo}/releases/tags/{Uri.EscapeDataString(asset.Tag)}";
    }

    /// <summary>
    /// The API URL of the named asset (<c>assets[].url</c>), which redirects to
    /// the file. <c>browser_download_url</c> is the gateway link that just failed,
    /// so it is not used.
    /// </summary>
    internal static string? FindAssetApiUrl(string json, string fileName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("assets", out var assets))
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                if (
                    !asset.TryGetProperty("name", out var name)
                    || !string.Equals(name.GetString(), fileName, StringComparison.Ordinal)
                )
                    continue;

                if (!asset.TryGetProperty("url", out var url))
                    return null;

                var apiUrl = url.GetString();
                return string.IsNullOrWhiteSpace(apiUrl) ? null : apiUrl;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
