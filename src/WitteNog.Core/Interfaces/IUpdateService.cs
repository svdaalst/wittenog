namespace WitteNog.Core.Interfaces;

public interface IUpdateService
{
    Task<string?> CheckForUpdateAsync();
    Task DownloadAndApplyUpdateAsync(string version, IProgress<int> progress);
}
