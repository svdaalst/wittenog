namespace WitteNog.App.Services;

using WitteNog.App.Models;

public class NavigationService : INavigationService
{
    private readonly List<TabViewModel> _tabs = new();
    private string _activeTabId = string.Empty;

    public IReadOnlyList<TabViewModel> Tabs => _tabs.AsReadOnly();
    public TabViewModel? ActiveTab => _tabs.FirstOrDefault(t => t.Id == _activeTabId);
    public event Action? TabsChanged;

    public NavigationService()
    {
        var today = DateTimeOffset.Now.ToString("yyyy-MM-dd");
        OpenNewTab(TabType.DailyPage, today);
    }

    public void OpenTab(TabType type, string query)
    {
        var existing = _tabs.FirstOrDefault(t => t.Type == type && t.Query == query);
        if (existing != null) { SetActiveTab(existing.Id); return; }
        OpenNewTab(type, query);
    }

    public void OpenNewTab(TabType type, string query)
    {
        var id = Guid.NewGuid().ToString();
        _tabs.Add(new TabViewModel(id, type, query, query));
        _activeTabId = id;
        TabsChanged?.Invoke();
    }

    public void CloseTab(string tabId)
    {
        var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null || _tabs.Count == 1) return;
        var idx = _tabs.IndexOf(tab);
        _tabs.Remove(tab);
        if (_activeTabId == tabId)
            _activeTabId = _tabs[Math.Max(0, idx - 1)].Id;
        TabsChanged?.Invoke();
    }

    public void SetActiveTab(string tabId)
    {
        if (_tabs.Any(t => t.Id == tabId))
        {
            _activeTabId = tabId;
            TabsChanged?.Invoke();
        }
    }
}
