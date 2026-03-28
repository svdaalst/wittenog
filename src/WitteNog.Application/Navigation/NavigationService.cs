namespace WitteNog.Application.Navigation;

public sealed class NavigationService
{
    private readonly List<TabViewModel> _tabs = new();

    public IReadOnlyList<TabViewModel> OpenTabs => _tabs.AsReadOnly();
    public TabViewModel? ActiveTab { get; private set; }

    public event EventHandler<TabViewModel>? TabOpened;
    public event EventHandler<TabViewModel>? TabClosed;
    public event EventHandler<TabViewModel?>? ActiveTabChanged;

    public void OpenTab(string pageKey, PageType type)
    {
        var existing = _tabs.FirstOrDefault(
            t => t.PageKey == pageKey && t.Type == type);

        if (existing is not null)
        {
            Activate(existing);
            return;
        }

        var tab = new TabViewModel(pageKey, type);
        _tabs.Add(tab);
        TabOpened?.Invoke(this, tab);
        Activate(tab);
    }

    public void CloseTab(Guid tabId)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab is null) return;

        var wasActive = tab.IsActive;
        var index = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        TabClosed?.Invoke(this, tab);

        if (!wasActive) return;

        var next = index > 0 ? _tabs.ElementAtOrDefault(index - 1)
                             : _tabs.FirstOrDefault();
        if (next is not null)
            Activate(next);
        else
        {
            ActiveTab = null;
            ActiveTabChanged?.Invoke(this, null);
        }
    }

    public void SwitchToTab(Guid tabId)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab is null) return;
        Activate(tab);
    }

    private void Activate(TabViewModel tab)
    {
        if (ActiveTab == tab) return;
        ActiveTab?.Deactivate();
        tab.Activate();
        ActiveTab = tab;
        ActiveTabChanged?.Invoke(this, tab);
    }
}
