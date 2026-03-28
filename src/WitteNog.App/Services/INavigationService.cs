namespace WitteNog.App.Services;

using WitteNog.App.Models;

public interface INavigationService
{
    IReadOnlyList<TabViewModel> Tabs { get; }
    TabViewModel? ActiveTab { get; }
    void OpenTab(TabType type, string query);
    void OpenNewTab(TabType type, string query);
    void CloseTab(string tabId);
    void SetActiveTab(string tabId);
    event Action TabsChanged;
}
