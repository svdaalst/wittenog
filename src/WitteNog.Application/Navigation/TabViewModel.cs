namespace WitteNog.Application.Navigation;

public sealed class TabViewModel
{
    public Guid Id { get; } = Guid.NewGuid();
    public string PageKey { get; }
    public PageType Type { get; }
    public string Title { get; }
    public bool IsActive { get; private set; }

    public TabViewModel(string pageKey, PageType type)
    {
        PageKey = pageKey;
        Type = type;
        Title = pageKey;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
