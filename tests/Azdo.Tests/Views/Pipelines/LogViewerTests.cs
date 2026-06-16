using Azdo.Tui.Runtime;
using Azdo.Tui.Views.Pipelines;
using Xunit;
using StyleSet = Azdo.Tui.Styles.Styles;

namespace Azdo.Tests.Views.Pipelines;

public class LogViewerTests
{
    private static StyleSet S => StyleSet.Default();

    private static LogViewerModel New(string title = "npm install")
        => new(null, 123, 5, title, S);

    [Fact]
    public void LoadingState_TrueUntilContentSet()
    {
        var model = New("Test Task");
        Assert.True(model.IsLoading());
        model.SetContent("Some content");
        Assert.False(model.IsLoading());
    }

    [Fact]
    public void ErrorState_SetAndRead()
    {
        var model = New("Test Task");
        Assert.Null(model.GetError());
        model.SetError("Failed to fetch logs");
        Assert.NotNull(model.GetError());
        Assert.Equal("Failed to fetch logs", model.GetError()!.Message);
    }

    [Fact]
    public void View_LoadingShowsTaskName()
    {
        var model = New("npm install");
        model.SetSize(80, 24);
        var view = model.View();
        Assert.Contains("Loading", view);
        Assert.Contains("npm install", view);
    }

    [Fact]
    public void View_WithContent_ShowsTaskNameHeader()
    {
        var model = New("npm install");
        model.SetSize(80, 24);
        model.SetContent("Build output line 1\nBuild output line 2");
        Assert.Contains("npm install", model.View());
    }

    [Fact]
    public void View_EmptyContent_ShowsNoLogContent()
    {
        var model = New("Test Task");
        model.SetSize(80, 24);
        model.SetContent("");
        Assert.Contains("No log content", model.View());
    }

    [Fact]
    public void ViewportUsesFullAvailableHeight()
    {
        var model = New("npm install");
        const int height = 30;
        model.SetSize(80, height);

        var lines = Enumerable.Range(0, 100).Select(i => "Log line " + (char)('A' + i % 26));
        model.SetContent(string.Join("\n", lines));

        var output = model.View().Split('\n');
        Assert.Equal(height, output.Length);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("Hello world", 1)]
    [InlineData("Line 1\nLine 2\nLine 3", 3)]
    [InlineData("Line 1\nLine 2\n", 2)]
    public void FormatLogLines_CountsLines(string content, int wantLen)
    {
        Assert.Equal(wantLen, LogViewerModel.FormatLogLines(content).Count);
    }

    [Theory]
    [InlineData("Hello world", "Hello world")]
    [InlineData("2024-02-06T10:00:00.000Z Starting build...", "Starting build...")]
    [InlineData("2024-02-06T10:00:00.123456Z npm install", "npm install")]
    [InlineData("  Added 1234 packages", "  Added 1234 packages")]
    public void StripTimestamp_RemovesAzureDevOpsPrefix(string input, string want)
    {
        Assert.Equal(want, LogViewerModel.StripTimestamp(input));
    }

    [Fact]
    public void GetContextItems_IncludesScrollAndTopBottom()
    {
        var items = New("Test Task").GetContextItems();
        Assert.NotEmpty(items);
        Assert.Contains(items, i => i.Key.Contains("↑↓") || i.Description.Contains("scroll"));
        Assert.Contains(items, i => i.Key.Contains("g/G") || i.Description.Contains("top/bottom"));
    }

    [Fact]
    public void GetScrollPercent_WithinBounds_AndAdvancesOnScroll()
    {
        var model = New("Test Task");
        model.SetSize(80, 20);
        var lines = Enumerable.Range(0, 100).Select(i => "Log line " + (i % 10));
        model.SetContent(string.Join("\n", lines));

        Assert.Equal(0, model.GetScrollPercent());

        model.Update(KeyMsg.Named("G")); // bottom
        Assert.Equal(100, model.GetScrollPercent());

        model.Update(KeyMsg.Named("g")); // top
        Assert.Equal(0, model.GetScrollPercent());
    }

    [Fact]
    public void Search_FiltersVisibleLines()
    {
        var model = New("Test Task");
        model.SetSize(80, 20);
        model.SetContent("apple\nbanana\napricot\ncherry");

        model.Update(KeyMsg.Named("f"));
        Assert.True(model.IsSearching());
        model.Update(KeyMsg.Rune_('a'));
        model.Update(KeyMsg.Rune_('p'));
        // "ap" matches apple + apricot.
        Assert.Contains("2/4", model.View());

        model.Update(KeyMsg.Named("esc"));
        Assert.False(model.IsSearching());
    }

    [Fact]
    public void Accessors_ReturnConstructorValues()
    {
        var model = New("Build");
        Assert.Equal(123, model.GetBuildId());
        Assert.Equal(5, model.GetLogId());
        Assert.Equal("Build", model.GetTitle());
        model.SetContent("raw content");
        Assert.Equal("raw content", model.GetContent());
    }
}
