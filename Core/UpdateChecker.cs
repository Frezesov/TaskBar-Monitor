using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TaskbarMonitor.Core;

public sealed record ReleaseInfo(Version Version, string Url);

/// <summary>
/// Asks GitHub which release is the latest one. Nothing is downloaded or installed: the user gets a link
/// to the release page, since the exe is unsigned and SmartScreen asks about it anyway.
/// </summary>
internal static class UpdateChecker
{
    public const string ReleasesPage = "https://github.com/Frezesov/TaskBar-Monitor/releases";
    private const string LatestApi = "https://api.github.com/repos/Frezesov/TaskBar-Monitor/releases/latest";

    public static Version Current { get; } = Normalize(typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(1, 0, 0));

    public static async Task<ReleaseInfo> GetLatestAsync(CancellationToken token)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TaskBarMonitor", Current.ToString(3)));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await http.GetAsync(LatestApi, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);

        var root = json.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseVersion(tag, out var version))
            throw new FormatException($"Unexpected release tag «{tag}».");
        string? url = root.TryGetProperty("html_url", out var link) ? link.GetString() : null;
        return new ReleaseInfo(version, IsReleaseLink(url) ? url! : ReleasePage(version));
    }

    public static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text) || !Version.TryParse(text.Trim().TrimStart('v', 'V'), out var parsed))
            return false;
        version = Normalize(parsed);
        return true;
    }

    public static string ReleasePage(Version version) => $"{ReleasesPage}/tag/v{version.ToString(3)}";

    // The link is opened through the shell, so only this repository's pages are accepted.
    private static bool IsReleaseLink(string? url) =>
        url is not null && url.StartsWith(ReleasesPage + "/", StringComparison.OrdinalIgnoreCase)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    // "1.1.0" and the assembly's "1.1.0.0" must compare as equal.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));
}
