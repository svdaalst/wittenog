using WitteNog.Application.Navigation;

namespace WitteNog.Application.Tests.Navigation;

public class TabViewModelTests
{
    [Fact]
    public void NewTab_HasUniqueId()
    {
        var a = new TabViewModel("2026-03-18", PageType.Daily);
        var b = new TabViewModel("2026-03-19", PageType.Daily);

        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void NewDailyTab_TitleMatchesPageKey()
    {
        var tab = new TabViewModel("2026-03-18", PageType.Daily);

        Assert.Equal("2026-03-18", tab.Title);
        Assert.Equal("2026-03-18", tab.PageKey);
        Assert.Equal(PageType.Daily, tab.Type);
    }

    [Fact]
    public void NewTopicTab_TitleMatchesPageKey()
    {
        var tab = new TabViewModel("ProjectX", PageType.Topic);

        Assert.Equal("ProjectX", tab.Title);
        Assert.Equal(PageType.Topic, tab.Type);
    }

    [Fact]
    public void NewTab_IsNotActiveByDefault()
    {
        var tab = new TabViewModel("2026-03-18", PageType.Daily);

        Assert.False(tab.IsActive);
    }

    [Fact]
    public void Activate_SetsIsActiveTrue()
    {
        var tab = new TabViewModel("2026-03-18", PageType.Daily);
        tab.Activate();

        Assert.True(tab.IsActive);
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var tab = new TabViewModel("2026-03-18", PageType.Daily);
        tab.Activate();
        tab.Deactivate();

        Assert.False(tab.IsActive);
    }
}
