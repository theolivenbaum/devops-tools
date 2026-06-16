using Azdo.Tui.Styles;
using Xunit;

namespace Azdo.Tests.Styles;

public class ThemesTests
{
    [Fact]
    public void DefaultIsDracula() => Assert.Equal("dracula", Themes.Default.Name);

    [Theory]
    [InlineData("dark")]
    [InlineData("gruvbox")]
    [InlineData("nord")]
    [InlineData("dracula")]
    [InlineData("catppuccin")]
    [InlineData("github")]
    [InlineData("retro")]
    [InlineData("monokai")]
    public void BuiltInThemesResolve(string name)
    {
        var t = Themes.GetByName(name);
        Assert.Equal(name, t.Name);
    }

    [Fact]
    public void UnknownThemeThrows() => Assert.Throws<ThemeException>(() => Themes.GetByName("nope"));

    [Fact]
    public void ListAvailableIsSorted()
    {
        var list = Themes.ListAvailable();
        Assert.Equal(list.OrderBy(x => x, StringComparer.Ordinal), list);
        Assert.Contains("dracula", list);
    }

    [Fact]
    public void LoadFromJson_Roundtrips()
    {
        var json = """{"name":"custom","primary":"#ffffff","border":"#333333"}""";
        var t = Themes.LoadFromJson(json);
        Assert.Equal("custom", t.Name);
        Assert.Equal("#ffffff", t.Primary);
    }

    [Fact]
    public void LoadFromJson_RequiresName()
        => Assert.Throws<ThemeException>(() => Themes.LoadFromJson("""{"primary":"#fff"}"""));

    [Fact]
    public void Styles_BuildFromTheme()
    {
        var s = new Azdo.Tui.Styles.Styles(Themes.Dracula);
        Assert.Equal("dracula", s.Theme.Name);
        var rendered = s.Header.Render("hi");
        Assert.Contains("\x1b[", rendered);
    }
}
