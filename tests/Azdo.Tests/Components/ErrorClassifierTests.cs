using Azdo.Core.AzureDevOps;
using Azdo.Tui.Components;
using Xunit;

namespace Azdo.Tests.Components;

/// <summary>
/// Tests for <see cref="ErrorClassifier"/>. A missing-scope (HTTP 403) error
/// must be treated as a per-feature limitation that degrades gracefully — never
/// as an app-blocking critical error — so the features whose scopes ARE present
/// keep working.
/// </summary>
public class ErrorClassifierTests
{
    [Fact]
    public void Classify_Null_ReturnsNull() => Assert.Null(ErrorClassifier.Classify(null));

    [Theory]
    [InlineData("access denied (HTTP 403): your PAT does not have sufficient permissions")]
    [InlineData("all projects failed: [access denied (HTTP 403): insufficient permissions]")]
    public void Classify_PermissionError_ReturnsNull_NotCritical(string message)
    {
        // A 403 affects only the feature whose scope is missing; it must not pop
        // the full-screen modal that would block every tab.
        Assert.Null(ErrorClassifier.Classify(new Exception(message)));
    }

    [Fact]
    public void Classify_Auth401_ReturnsCritical()
    {
        var info = ErrorClassifier.Classify(new Exception("authentication failed (HTTP 401): your PAT may be expired"));
        Assert.NotNull(info);
        Assert.Equal("Authentication Error", info!.Title);
    }

    [Fact]
    public void Classify_404_ReturnsCritical()
    {
        var info = ErrorClassifier.Classify(new Exception("resource not found (HTTP 404): the requested resource does not exist"));
        Assert.NotNull(info);
        Assert.Equal("Configuration Error", info!.Title);
    }

    [Fact]
    public void Classify_400_ReturnsCritical()
    {
        var info = ErrorClassifier.Classify(new Exception("all projects failed: [HTTP request failed with status 400]"));
        Assert.NotNull(info);
        Assert.Equal("Configuration Error", info!.Title);
    }

    [Fact]
    public void Classify_TransientError_ReturnsNull() =>
        Assert.Null(ErrorClassifier.Classify(new Exception("connection timeout")));

    [Theory]
    [InlineData("access denied (HTTP 403): insufficient permissions", true)]
    [InlineData("all projects failed: [access denied (HTTP 403)]", true)]
    [InlineData("authentication failed (HTTP 401)", false)]
    [InlineData("resource not found (HTTP 404)", false)]
    [InlineData("connection timeout", false)]
    public void IsPermissionError_MatchesOnly403(string message, bool expected) =>
        Assert.Equal(expected, ErrorClassifier.IsPermissionError(new Exception(message)));

    [Fact]
    public void IsPermissionError_Null_False() => Assert.False(ErrorClassifier.IsPermissionError(null));

    [Fact]
    public void IsPermissionError_FromFormatHttpError403_True() =>
        Assert.True(ErrorClassifier.IsPermissionError(Client.FormatHttpError(403)));

    [Fact]
    public void NewCriticalErrorCmd_PermissionError_ReturnsNull() =>
        Assert.Null(ErrorClassifier.NewCriticalErrorCmd(Client.FormatHttpError(403)));
}
