using System.Net;
using System.Text;
using Azdo.Core.AzureDevOps;
using Xunit;

namespace Azdo.Tests.AzureDevOps;

public class ClientTests
{
    [Theory]
    [InlineData("", "myproject", "pat", "organization")]
    [InlineData("myorg", "", "pat", "project")]
    [InlineData("myorg", "myproject", "", "PAT")]
    public void NewClient_Validation(string org, string project, string pat, string contains)
    {
        var ex = Assert.Throws<ArgumentException>(() => new Client(org, project, pat));
        Assert.Contains(contains, ex.Message);
    }

    [Fact]
    public void NewClient_Valid_SetsBaseUrl()
    {
        var c = new Client("myorg", "myproject", "test-pat");
        Assert.Equal("https://dev.azure.com/myorg/myproject/_apis", c.BaseUrl);
    }

    [Fact]
    public async Task AuthHeader_IsBasicWithColonPat()
    {
        const string pat = "my-secret-token";
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{\"value\": []}");
        var client = new Client("myorg", "myproject", pat, handler);

        await client.GetAsync("/test");

        var req = Assert.Single(handler.Requests);
        Assert.NotNull(req.Authorization);
        Assert.StartsWith("Basic ", req.Authorization);
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(req.Authorization!["Basic ".Length..]));
        Assert.Equal(":" + pat, decoded);
    }

    [Fact]
    public async Task Get_Success_ReturnsBody()
    {
        const string body = "{\"id\": \"123\", \"name\": \"test-item\"}";
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, body);
        var client = TestHelpers.NewClient(handler);

        var got = await client.GetAsync("/test/endpoint");

        Assert.Equal(body, got);
        var req = Assert.Single(handler.Requests);
        Assert.Equal("GET", req.Method);
        Assert.Equal("/test-org/test-project/_apis/test/endpoint", req.Uri.AbsolutePath);
    }

    [Fact]
    public async Task Get_401_MentionsPatExpiredInvalid()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.Unauthorized, "{\"secret\":\"x\"}");
        var client = TestHelpers.NewClient(handler);
        var ex = await Assert.ThrowsAsync<AzdoHttpException>(() => client.GetAsync("/test"));
        Assert.Contains("PAT", ex.Message);
        Assert.Contains("expired", ex.Message);
        Assert.Contains("invalid", ex.Message);
    }

    [Fact]
    public async Task Get_403_MentionsPermissions()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.Forbidden, "{}");
        var client = TestHelpers.NewClient(handler);
        var ex = await Assert.ThrowsAsync<AzdoHttpException>(() => client.GetAsync("/test"));
        Assert.Contains("PAT", ex.Message);
        Assert.Contains("permission", ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ErrorMessages_DoNotLeakResponseBody(HttpStatusCode status)
    {
        const string sensitive = "SENSITIVE_SECRET_DATA_12345";
        var handler = FakeHttpMessageHandler.Constant(status, $"{{\"secret\":\"{sensitive}\"}}");
        var client = TestHelpers.NewClient(handler);
        var ex = await Assert.ThrowsAsync<AzdoHttpException>(() => client.GetAsync("/test"));
        Assert.DoesNotContain(sensitive, ex.Message);
    }

    [Fact]
    public async Task Post_SendsJsonContentType()
    {
        // Content-Type is meaningful on requests with a body. (Go sets it on GET
        // too, but HTTP/HttpClient only carry Content-Type alongside content.)
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{\"workItems\":[]}");
        var client = TestHelpers.NewClient(handler);
        await client.QueryWorkItemIdsAsync("SELECT [System.Id] FROM WorkItems", 50);
        var req = Assert.Single(handler.Requests);
        Assert.NotNull(req.ContentType);
        Assert.Contains("application/json", req.ContentType);
    }

    [Fact]
    public void FormatHttpError_NotFound()
    {
        var err = Client.FormatHttpError(404);
        Assert.Contains("404", err.Message);
        Assert.Contains("not found", err.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            err.Message.Contains("organization", StringComparison.OrdinalIgnoreCase) ||
            err.Message.Contains("project", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FormatHttpError_RateLimit()
    {
        var err = Client.FormatHttpError(429);
        Assert.Contains("429", err.Message);
        Assert.Contains("rate limit", err.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            err.Message.Contains("wait", StringComparison.OrdinalIgnoreCase) ||
            err.Message.Contains("retry", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FormatHttpError_ServerError()
    {
        var err = Client.FormatHttpError(500);
        Assert.Contains("500", err.Message);
        Assert.Contains("Azure DevOps", err.Message);
    }

    [Fact]
    public void FormatHttpError_ServiceUnavailable()
    {
        var err = Client.FormatHttpError(503);
        Assert.Contains("503", err.Message);
        Assert.Contains("unavailable", err.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("temporary", err.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatHttpError_Default()
    {
        var err = Client.FormatHttpError(418);
        Assert.Contains("418", err.Message);
    }

    [Fact]
    public async Task GetCurrentUserId_Caching_SkipsNetworkCall()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{\"authenticatedUser\":{\"id\":\"user-123\"}}");
        var client = TestHelpers.NewClient(handler);
        client.SetUserID("user-123");

        var id1 = await client.GetCurrentUserIdAsync();
        var id2 = await client.GetCurrentUserIdAsync();

        Assert.Equal(id1, id2);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetCurrentUserId_FetchesAndParses()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{\"authenticatedUser\":{\"id\":\"user-xyz\"}}");
        var client = TestHelpers.NewClient(handler);

        var id = await client.GetCurrentUserIdAsync();

        Assert.Equal("user-xyz", id);
        var req = Assert.Single(handler.Requests);
        Assert.EndsWith("/test-org/_apis/connectionData", req.Uri.AbsolutePath);
    }
}
