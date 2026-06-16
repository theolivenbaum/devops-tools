using System.Net;
using System.Text;
using System.Text.Json;

namespace Azdo.Core.AzureDevOps;

/// <summary>An Azure DevOps API client scoped to a single organization/project.</summary>
public sealed partial class Client : IAzdoClient
{
    private readonly string _org;
    private readonly string _project;
    private readonly string _pat;
    private string _baseUrl;
    private readonly HttpClient _httpClient;
    private string _userId = ""; // cached authenticated user ID

    internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var o = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        o.Converters.Add(new UtcDateTimeConverter());
        o.Converters.Add(new UtcNullableDateTimeConverter());
        return o;
    }

    /// <summary>
    /// Creates a new Azure DevOps API client. Optionally accepts an
    /// <see cref="HttpMessageHandler"/> (for testing with canned responses) or a
    /// fully configured <see cref="HttpClient"/>.
    /// </summary>
    public Client(string org, string project, string pat, HttpMessageHandler? handler = null, HttpClient? httpClient = null)
    {
        if (string.IsNullOrEmpty(org))
            throw new ArgumentException("organization cannot be empty", nameof(org));
        if (string.IsNullOrEmpty(project))
            throw new ArgumentException("project cannot be empty", nameof(project));
        if (string.IsNullOrEmpty(pat))
            throw new ArgumentException("PAT cannot be empty", nameof(pat));

        _org = org;
        _project = project;
        _pat = pat;
        _baseUrl = $"https://dev.azure.com/{org}/{project}/_apis";

        if (httpClient is not null)
        {
            _httpClient = httpClient;
        }
        else
        {
            _httpClient = handler is not null ? new HttpClient(handler) : new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }
    }

    /// <summary>The organization name.</summary>
    public string GetOrg() => _org;

    /// <summary>The project name.</summary>
    public string GetProject() => _project;

    /// <summary>The base URL, exposed for testing.</summary>
    internal string BaseUrl => _baseUrl;

    /// <summary>Overrides the base URL for the client (used by demo mode / tests).</summary>
    public void SetBaseURL(string url) => _baseUrl = url;

    /// <summary>Sets the cached user ID, bypassing the connectionData API call (demo mode / tests).</summary>
    public void SetUserID(string id) => _userId = id;

    // ----- HTTP helpers -----

    private void SetAuthHeader(HttpRequestMessage req)
    {
        // Azure DevOps uses the format ":{PAT}" for basic auth.
        var auth = ":" + _pat;
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(auth));
        req.Headers.TryAddWithoutValidation("Authorization", "Basic " + encoded);
    }

    /// <summary>Performs a GET request and returns the raw response body.</summary>
    internal async Task<string> GetAsync(string path, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
        SetAuthHeader(req);
        req.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        return await SendAsync(req, ct).ConfigureAwait(false);
    }

    /// <summary>Performs a GET request with a custom Accept header, returning the raw body.</summary>
    internal async Task<string> GetRawAsync(string path, string accept, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
        SetAuthHeader(req);
        req.Headers.TryAddWithoutValidation("Accept", accept);
        return await SendAsync(req, ct).ConfigureAwait(false);
    }

    internal Task<string> PutAsync(string path, string? body, CancellationToken ct = default) =>
        DoRequestAsync(HttpMethod.Put, path, body, "application/json", ct);

    internal Task<string> PatchAsync(string path, string? body, CancellationToken ct = default) =>
        DoRequestAsync(new HttpMethod("PATCH"), path, body, "application/json", ct);

    internal Task<string> PostAsync(string path, string? body, CancellationToken ct = default) =>
        DoRequestAsync(HttpMethod.Post, path, body, "application/json", ct);

    internal async Task<string> DoRequestWithContentTypeAsync(HttpMethod method, string path, string? body, string contentType, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(method, _baseUrl + path);
        SetAuthHeader(req);
        if (body is not null)
            req.Content = new StringContent(body, Encoding.UTF8);
        // Set Content-Type explicitly (overriding StringContent's default charset behavior).
        if (req.Content is not null)
            req.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        else
            req.Headers.TryAddWithoutValidation("Content-Type", contentType);
        return await SendAsync(req, ct).ConfigureAwait(false);
    }

    private async Task<string> DoRequestAsync(HttpMethod method, string path, string? body, string contentType, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, _baseUrl + path);
        SetAuthHeader(req);
        if (body is not null)
        {
            req.Content = new StringContent(body, Encoding.UTF8);
            req.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        }
        else
        {
            req.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }
        return await SendAsync(req, ct).ConfigureAwait(false);
    }

    private async Task<string> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        int code = (int)resp.StatusCode;
        if (code < 200 || code >= 300)
            throw FormatHttpError(code);
        return body;
    }

    /// <summary>
    /// Creates a user-friendly error based on the HTTP status code. The response
    /// body is never included in the message.
    /// </summary>
    public static AzdoHttpException FormatHttpError(int statusCode) => statusCode switch
    {
        (int)HttpStatusCode.Unauthorized => new AzdoHttpException(statusCode,
            "authentication failed (HTTP 401): your PAT may be expired or invalid. " +
            "Please generate a new PAT in Azure DevOps and update your configuration"),
        (int)HttpStatusCode.Forbidden => new AzdoHttpException(statusCode,
            "access denied (HTTP 403): your PAT does not have sufficient permissions. " +
            "Required scopes: Code (Read), Build (Read), Work Items (Read & Write)"),
        (int)HttpStatusCode.NotFound => new AzdoHttpException(statusCode,
            "resource not found (HTTP 404): the requested resource does not exist. " +
            "Please verify your organization and project names are correct in your configuration"),
        (int)HttpStatusCode.TooManyRequests => new AzdoHttpException(statusCode,
            "rate limit exceeded (HTTP 429): too many requests to Azure DevOps. " +
            "Please wait a few minutes before retrying"),
        (int)HttpStatusCode.InternalServerError => new AzdoHttpException(statusCode,
            "server error (HTTP 500): Azure DevOps encountered an internal error. " +
            "This is usually temporary - please try again in a few moments"),
        (int)HttpStatusCode.ServiceUnavailable => new AzdoHttpException(statusCode,
            "service unavailable (HTTP 503): Azure DevOps is temporarily unavailable. " +
            "This is usually a temporary issue - please try again later"),
        _ => new AzdoHttpException(statusCode, $"HTTP request failed with status {statusCode}"),
    };

    /// <summary>
    /// Returns the authenticated user's ID, fetching and caching it on first call.
    /// Connection data is at the org level, not project-scoped.
    /// </summary>
    public async Task<string> GetCurrentUserIdAsync(CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(_userId))
            return _userId;

        var url = $"https://dev.azure.com/{_org}/_apis/connectionData";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        SetAuthHeader(req);
        req.Headers.TryAddWithoutValidation("Content-Type", "application/json");

        string body;
        using (var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false))
        {
            body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            int code = (int)resp.StatusCode;
            if (code < 200 || code >= 300)
                throw FormatHttpError(code);
        }

        ConnectionDataResponse? data;
        try
        {
            data = JsonSerializer.Deserialize<ConnectionDataResponse>(body, JsonOptions);
        }
        catch (JsonException e)
        {
            throw new InvalidOperationException($"failed to parse connection data: {e.Message}", e);
        }

        var id = data?.AuthenticatedUser.Id ?? "";
        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException("connection data did not contain a user ID");

        _userId = id;
        return _userId;
    }

    /// <summary>Escapes a string for embedding in a JSON payload (mirrors Go's json.Marshal of a string).</summary>
    internal static string EscapeJsonString(string s) => JsonSerializer.Serialize(s);
}
