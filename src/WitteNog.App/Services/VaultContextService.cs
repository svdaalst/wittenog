namespace WitteNog.App.Services;

public class VaultContextService
{
    private const string PrefsKey = "VaultPath";

    public string? VaultPath { get; private set; }
    public bool IsConfigured => !string.IsNullOrEmpty(VaultPath);
    public event Action? VaultPathChanged;

    public VaultContextService()
    {
        VaultPath = Preferences.Default.Get<string?>(PrefsKey, null);
    }

    public void SetVaultPath(string path)
    {
        VaultPath = path;
        Preferences.Default.Set(PrefsKey, path);
        VaultPathChanged?.Invoke();
    }
}
