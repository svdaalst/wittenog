using System.Text.Json;

namespace WitteNog.App.Services;

public class VaultContextService
{
    private const string PrefsKey = "VaultPath";
    private const string VaultPathsKey = "VaultPaths";

    public string? VaultPath { get; private set; }
    public IReadOnlyList<string> VaultPaths { get; private set; }
    public bool IsConfigured => !string.IsNullOrEmpty(VaultPath);

    public event Action? VaultPathChanged;
    public event Action? VaultListChanged;

    public VaultContextService()
    {
        VaultPath = Preferences.Default.Get<string?>(PrefsKey, null);

        var json = Preferences.Default.Get<string?>(VaultPathsKey, null);
        var paths = json is not null
            ? JsonSerializer.Deserialize<List<string>>(json) ?? []
            : [];

        if (VaultPath is not null && !paths.Contains(VaultPath))
            paths.Insert(0, VaultPath);

        VaultPaths = paths.AsReadOnly();
    }

    public void SetVaultPath(string path)
    {
        VaultPath = path;
        Preferences.Default.Set(PrefsKey, path);
        AddVaultToList(path);
        VaultPathChanged?.Invoke();
    }

    public void AddVault(string path)
    {
        AddVaultToList(path);
        SetVaultPath(path);
    }

    public void RemoveVault(string path)
    {
        if (path == VaultPath) return;
        var list = VaultPaths.ToList();
        if (!list.Remove(path)) return;
        VaultPaths = list.AsReadOnly();
        SaveVaultPaths();
        VaultListChanged?.Invoke();
    }

    private void AddVaultToList(string path)
    {
        if (VaultPaths.Contains(path)) return;
        var list = VaultPaths.ToList();
        list.Add(path);
        VaultPaths = list.AsReadOnly();
        SaveVaultPaths();
        VaultListChanged?.Invoke();
    }

    private void SaveVaultPaths()
    {
        Preferences.Default.Set(VaultPathsKey, JsonSerializer.Serialize(VaultPaths.ToList()));
    }
}
