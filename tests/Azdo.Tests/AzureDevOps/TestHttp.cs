using System.Net;
using System.Text;
using System.Text.Json;
using Azdo.Core.AzureDevOps;

namespace Azdo.Tests.AzureDevOps;

/// <summary>
/// A fake <see cref="HttpMessageHandler"/> that returns canned responses and
/// records the requests it received.
/// </summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, (HttpStatusCode Status, string Body, string? ContentType)> _responder;

    public List<RecordedRequest> Requests { get; } = new();

    public FakeHttpMessageHandler(Func<HttpRequestMessage, string, (HttpStatusCode, string, string?)> responder)
        => _responder = responder;

    /// <summary>Convenience: always returns the same status + JSON body.</summary>
    public static FakeHttpMessageHandler Constant(HttpStatusCode status, string body, string? contentType = "application/json")
        => new((_, _) => (status, body, contentType));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string body = "";
        if (request.Content is not null)
            body = await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        Requests.Add(new RecordedRequest(
            request.Method.Method,
            request.RequestUri!,
            body,
            request.Headers.TryGetValues("Authorization", out var auth) ? string.Join(",", auth) : null,
            request.Content?.Headers.ContentType?.ToString()
                ?? (request.Headers.TryGetValues("Content-Type", out var ct) ? string.Join(",", ct) : null),
            request.Headers.TryGetValues("Accept", out var accept) ? string.Join(",", accept) : null));

        var (status, respBody, contentType) = _responder(request, body);
        var resp = new HttpResponseMessage(status)
        {
            Content = new StringContent(respBody, Encoding.UTF8, contentType ?? "application/json"),
        };
        return resp;
    }
}

public sealed record RecordedRequest(
    string Method,
    Uri Uri,
    string Body,
    string? Authorization,
    string? ContentType,
    string? Accept);

/// <summary>Shared helpers for building clients/serialization in tests.</summary>
public static class TestHelpers
{
    public static Client NewClient(FakeHttpMessageHandler handler, string org = "test-org", string project = "test-project", string pat = "test-pat")
        => new(org, project, pat, handler);

    public static string Json<T>(T value) => JsonSerializer.Serialize(value, Client.JsonOptions);

    public static DateTime Utc(string iso) =>
        DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal).UtcDateTime;
}
