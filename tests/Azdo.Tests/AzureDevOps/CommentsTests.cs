using System.Net;
using System.Text.Json;
using Azdo.Core.AzureDevOps;
using Xunit;

namespace Azdo.Tests.AzureDevOps;

public class CommentsTests
{
    [Fact]
    public async Task GetWorkItemComments_VerifiesQuery_AndParses()
    {
        const string body = """
        { "totalCount": 2, "count": 2, "comments": [
            { "id": 45, "text": "Newest comment", "createdBy": { "displayName": "Jane Doe", "id": "id-1" }, "createdDate": "2019-01-21T20:12:14.683Z" },
            { "id": 44, "text": "Older comment", "createdBy": { "displayName": "John Roe", "id": "id-2" }, "createdDate": "2019-01-20T23:26:33.383Z" }
        ]}
        """;
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, body);
        var client = TestHelpers.NewClient(handler);

        var comments = await client.GetWorkItemCommentsAsync(299);

        Assert.Equal("GET", handler.Requests[0].Method);
        var q = handler.Requests[0].Uri.Query;
        Assert.Contains("api-version=7.1-preview.4", q);
        Assert.Contains("order=desc", q);
        Assert.Contains("top=200", q);

        Assert.Equal(2, comments.Count);
        Assert.Equal("Newest comment", comments[0].Text);
        Assert.Equal("Jane Doe", comments[0].CreatedBy.DisplayName);
        Assert.NotEqual(default, comments[0].CreatedDate);
        Assert.Equal(45, comments[0].Id);
    }

    [Fact]
    public async Task GetWorkItemComments_ApiError()
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.InternalServerError, "{\"message\":\"boom\"}");
        var client = TestHelpers.NewClient(handler);
        await Assert.ThrowsAsync<AzdoHttpException>(() => client.GetWorkItemCommentsAsync(1));
    }

    [Fact]
    public async Task AddWorkItemComment_EscapesQuotes_AndParses()
    {
        const string body = """{ "id": 100, "text": "Hello \"world\"", "createdBy": { "displayName": "Jane Doe" }, "createdDate": "2019-01-21T20:12:14.683Z" }""";
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, body);
        var client = TestHelpers.NewClient(handler);

        var comment = await client.AddWorkItemCommentAsync(299, "Hello \"world\"");

        Assert.Equal("POST", handler.Requests[0].Method);
        Assert.Contains("api-version=7.1-preview.4", handler.Requests[0].Uri.Query);

        // Body must be valid JSON with an escaped "text" field.
        var payload = JsonSerializer.Deserialize<JsonElement>(handler.Requests[0].Body);
        Assert.Equal("Hello \"world\"", payload.GetProperty("text").GetString());

        Assert.Equal(100, comment.Id);
        Assert.Equal("Hello \"world\"", comment.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t  ")]
    public async Task AddWorkItemComment_RejectsEmpty_NoHttpCall(string text)
    {
        var handler = FakeHttpMessageHandler.Constant(HttpStatusCode.OK, "{}");
        var client = TestHelpers.NewClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => client.AddWorkItemCommentAsync(1, text));
        Assert.Empty(handler.Requests);
    }
}
