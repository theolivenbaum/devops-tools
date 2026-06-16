using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Azdo.Core.Version;

/// <summary>Result of a version check (≈ <c>version.UpdateInfo</c>).</summary>
public sealed record UpdateInfo
{
    public string CurrentVersion { get; init; } = "";
    public string LatestVersion { get; init; } = "";
    public bool UpdateAvailable { get; init; }
    public string ReleaseUrl { get; init; } = "";
}

/// <summary>Checks GitHub releases for a newer version (≈ <c>version.Checker</c>).</summary>
public sealed class VersionChecker
{
    private const string DefaultApiUrl = "https://api.github.com/repos/Elpulgo/azdo/releases/latest";

    private readonly string _currentVersion;
    private readonly string _apiUrl;
    private readonly HttpClient _http;

    public VersionChecker(string currentVersion, HttpClient? http = null, string? apiUrl = null)
    {
        _currentVersion = currentVersion;
        _apiUrl = apiUrl ?? DefaultApiUrl;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.Add("User-Agent", "azdo-tui");
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")] public string TagName { get; set; } = "";
        [JsonPropertyName("html_url")] public string HtmlUrl { get; set; } = "";
    }

    public async Task<UpdateInfo> CheckForUpdateAsync(CancellationToken ct = default)
    {
        // Don't check for dev builds.
        if (string.IsNullOrEmpty(_currentVersion) || _currentVersion == "dev")
            return new UpdateInfo { CurrentVersion = _currentVersion };

        using var req = new HttpRequestMessage(HttpMethod.Get, _apiUrl);
        req.Headers.Add("Accept", "application/vnd.github.v3+json");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"GitHub API returned status {(int)resp.StatusCode}");

        var release = await resp.Content.ReadFromJsonAsync<GitHubRelease>(ct).ConfigureAwait(false)
                      ?? new GitHubRelease();

        return new UpdateInfo
        {
            CurrentVersion = _currentVersion,
            LatestVersion = release.TagName,
            ReleaseUrl = release.HtmlUrl,
            UpdateAvailable = IsNewer(_currentVersion, release.TagName),
        };
    }

    public static bool IsNewer(string current, string latest)
    {
        var cur = ParseSemver(current);
        var lat = ParseSemver(latest);
        if (cur is null || lat is null) return false;
        for (int i = 0; i < 3; i++)
        {
            if (lat[i] > cur[i]) return true;
            if (lat[i] < cur[i]) return false;
        }
        return false;
    }

    private static int[]? ParseSemver(string v)
    {
        v = v.StartsWith('v') ? v[1..] : v;
        var parts = v.Split('.', 3);
        if (parts.Length != 3) return null;
        var result = new int[3];
        for (int i = 0; i < 3; i++)
        {
            var p = parts[i].Split('-', 2)[0];
            if (!int.TryParse(p, out result[i])) return null;
        }
        return result;
    }
}
