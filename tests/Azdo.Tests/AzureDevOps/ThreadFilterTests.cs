using Azdo.Core.AzureDevOps;
using Xunit;
using Thread = Azdo.Core.AzureDevOps.Thread;

namespace Azdo.Tests.AzureDevOps;

public class ThreadFilterTests
{
    private static Thread T(int id, params Comment[] comments) =>
        new() { Id = id, Status = "active", Comments = comments.ToList() };

    private static Comment C(int id, string content, string author = "") =>
        new() { Id = id, Content = content, Author = new Identity { DisplayName = author } };

    [Fact]
    public void FiltersOut_MicrosoftVisualStudio_ContentComments()
    {
        var threads = new[]
        {
            T(1, C(1, "This looks good!")),
            T(2, C(2, "Microsoft.VisualStudio.Services.CodeReview.PolicyViolation")),
            T(3, C(3, "Please fix this")),
        };
        var got = ThreadFilter.FilterSystemThreads(threads);
        Assert.Equal(new[] { 1, 3 }, got.Select(t => t.Id));
    }

    [Fact]
    public void Empty_ReturnsEmpty()
        => Assert.Empty(ThreadFilter.FilterSystemThreads(Array.Empty<Thread>()));

    [Fact]
    public void KeepsThreads_WithNoComments()
    {
        var got = ThreadFilter.FilterSystemThreads(new[] { T(1) });
        Assert.Single(got);
        Assert.Equal(1, got[0].Id);
    }

    [Fact]
    public void FiltersAll_SystemThreads()
    {
        var threads = new[]
        {
            T(1, C(1, "Microsoft.VisualStudio.Services.Something")),
            T(2, C(2, "Microsoft.VisualStudio.Another.Thing")),
        };
        Assert.Empty(ThreadFilter.FilterSystemThreads(threads));
    }

    [Fact]
    public void Filters_LeadingWhitespace_SystemComment()
    {
        var threads = new[] { T(1, C(1, "  Microsoft.VisualStudio.Services.TFS: Something")) };
        Assert.Empty(ThreadFilter.FilterSystemThreads(threads));
    }

    [Fact]
    public void Filters_Thread_IfAnyCommentIsSystem()
    {
        var threads = new[]
        {
            T(1, C(1, ""), C(2, "Microsoft.VisualStudio.Services.TFS: Updated reference")),
            T(2, C(3, "Real review comment")),
        };
        var got = ThreadFilter.FilterSystemThreads(threads);
        Assert.Equal(new[] { 2 }, got.Select(t => t.Id));
    }

    [Fact]
    public void Filters_BySystemAuthorName()
    {
        var threads = new[]
        {
            T(1, C(1, "The reference refs/heads/feature/test was updated.", "Microsoft.VisualStudio.Services.TFS")),
            T(2, C(2, "Looks good!", "John Doe")),
        };
        var got = ThreadFilter.FilterSystemThreads(threads);
        Assert.Equal(new[] { 2 }, got.Select(t => t.Id));
    }

    [Fact]
    public void Filters_PolicyStatusUpdate()
    {
        var threads = new[]
        {
            T(1, C(1, "Policy status has been updated.", "System")),
            T(2, C(2, "Please review this code", "John Doe")),
        };
        var got = ThreadFilter.FilterSystemThreads(threads);
        Assert.Equal(new[] { 2 }, got.Select(t => t.Id));
    }

    [Theory]
    [InlineData("John Doe voted -5", true)]
    [InlineData("Jane Smith voted 0", true)]
    [InlineData("Bob Wilson voted 10", true)]
    [InlineData("This is a real comment", false)]
    [InlineData("I voted in the election", false)]
    public void IsVotedComment(string content, bool want)
        => Assert.Equal(want, ThreadFilter.IsVotedComment(content));

    [Fact]
    public void Filters_VotedComments()
    {
        var threads = new[]
        {
            T(1, C(1, "Jane Smith voted 0", "System")),
        };
        Assert.Empty(ThreadFilter.FilterSystemThreads(threads));
    }
}
