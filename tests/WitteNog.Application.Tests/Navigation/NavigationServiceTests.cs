using WitteNog.Application.Navigation;

namespace WitteNog.Application.Tests.Navigation;

public class NavigationServiceTests
{
    // ── OpenTab ────────────────────────────────────────────────────────────

    [Fact]
    public void OpenTab_AddsTabToOpenTabs()
    {
        var nav = new NavigationService();
        nav.OpenTab("2026-03-18", PageType.Daily);

        Assert.Single(nav.OpenTabs);
    }

    [Fact]
    public void OpenTab_MakesNewTabActive()
    {
        var nav = new NavigationService();
        nav.OpenTab("2026-03-18", PageType.Daily);

        Assert.NotNull(nav.ActiveTab);
        Assert.Equal("2026-03-18", nav.ActiveTab!.PageKey);
        Assert.True(nav.ActiveTab.IsActive);
    }

    [Fact]
    public void OpenTab_SamePage_DoesNotDuplicate()
    {
        var nav = new NavigationService();
        nav.OpenTab("ProjectX", PageType.Topic);
        nav.OpenTab("ProjectX", PageType.Topic);

        Assert.Single(nav.OpenTabs);
    }

    [Fact]
    public void OpenTab_SamePage_ActivatesExistingTab()
    {
        var nav = new NavigationService();
        nav.OpenTab("2026-03-18", PageType.Daily);
        nav.OpenTab("ProjectX", PageType.Topic);
        nav.OpenTab("2026-03-18", PageType.Daily); // switch back

        Assert.Equal(2, nav.OpenTabs.Count);
        Assert.Equal("2026-03-18", nav.ActiveTab!.PageKey);
    }

    [Fact]
    public void OpenTab_DeactivatesPreviousTab()
    {
        var nav = new NavigationService();
        nav.OpenTab("2026-03-18", PageType.Daily);
        var first = nav.ActiveTab!;

        nav.OpenTab("ProjectX", PageType.Topic);

        Assert.False(first.IsActive);
    }

    [Fact]
    public void OpenTab_RaisesTabOpenedEvent_OnlyForNewTabs()
    {
        var nav = new NavigationService();
        var raised = new List<TabViewModel>();
        nav.TabOpened += (_, tab) => raised.Add(tab);

        nav.OpenTab("2026-03-18", PageType.Daily);
        nav.OpenTab("2026-03-18", PageType.Daily); // duplicate – no event

        Assert.Single(raised);
        Assert.Equal("2026-03-18", raised[0].PageKey);
    }

    // ── CloseTab ───────────────────────────────────────────────────────────

    [Fact]
    public void CloseTab_RemovesTabFromOpenTabs()
    {
        var nav = new NavigationService();
        nav.OpenTab("2026-03-18", PageType.Daily);
        var id = nav.ActiveTab!.Id;

        nav.CloseTab(id);

        Assert.Empty(nav.OpenTabs);
    }

    [Fact]
    public void CloseTab_ActiveTab_ActivatesPreviousTab()
    {
        var nav = new NavigationService();
        nav.OpenTab("2026-03-18", PageType.Daily);
        nav.OpenTab("ProjectX", PageType.Topic);
        var second = nav.ActiveTab!;

        nav.CloseTab(second.Id);

        Assert.Equal("2026-03-18", nav.ActiveTab!.PageKey);
    }

    [Fact]
    public void CloseTab_LastTab_SetsActiveTabNull()
    {
        var nav = new NavigationService();
        nav.OpenTab("2026-03-18", PageType.Daily);
        var id = nav.ActiveTab!.Id;

        nav.CloseTab(id);

        Assert.Null(nav.ActiveTab);
    }

    [Fact]
    public void CloseTab_UnknownId_DoesNotThrow()
    {
        var nav = new NavigationService();
        nav.OpenTab("2026-03-18", PageType.Daily);

        nav.CloseTab(Guid.NewGuid()); // must not throw
    }

    [Fact]
    public void CloseTab_RaisesTabClosedEvent()
    {
        var nav = new NavigationService();
        nav.OpenTab("2026-03-18", PageType.Daily);
        var id = nav.ActiveTab!.Id;
        TabViewModel? closed = null;
        nav.TabClosed += (_, tab) => closed = tab;

        nav.CloseTab(id);

        Assert.NotNull(closed);
        Assert.Equal(id, closed!.Id);
    }

    // ── SwitchToTab ────────────────────────────────────────────────────────

    [Fact]
    public void SwitchToTab_ActivatesCorrectTab()
    {
        var nav = new NavigationService();
        nav.OpenTab("2026-03-18", PageType.Daily);
        var first = nav.ActiveTab!;
        nav.OpenTab("ProjectX", PageType.Topic);

        nav.SwitchToTab(first.Id);

        Assert.Equal(first.Id, nav.ActiveTab!.Id);
        Assert.True(first.IsActive);
    }

    [Fact]
    public void SwitchToTab_UnknownId_DoesNotThrow()
    {
        var nav = new NavigationService();
        nav.SwitchToTab(Guid.NewGuid()); // must not throw
    }

    // ── ActiveTabChanged event ─────────────────────────────────────────────

    [Fact]
    public void ActiveTabChanged_FiresWhenSwitchingTabs()
    {
        var nav = new NavigationService();
        nav.OpenTab("2026-03-18", PageType.Daily);
        var first = nav.ActiveTab!;
        nav.OpenTab("ProjectX", PageType.Topic);

        TabViewModel? received = null;
        nav.ActiveTabChanged += (_, tab) => received = tab;

        nav.SwitchToTab(first.Id);

        Assert.Equal(first.Id, received!.Id);
    }
}
