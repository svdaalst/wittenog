namespace WitteNog.Core.Interfaces;

using WitteNog.Core.Models;

public interface IVaultSettings
{
    /// <summary>
    /// Returns the transcription settings for the given vault.
    /// Returns default <see cref="TranscriptionSettings"/> when the vault has no settings file.
    /// </summary>
    TranscriptionSettings GetTranscriptionSettings(string vaultPath);

    /// <summary>Persists the transcription settings for the given vault.</summary>
    void SaveTranscriptionSettings(string vaultPath, TranscriptionSettings settings);

    /// <summary>
    /// Returns the markdown template body used when auto-creating a new daily note,
    /// or <c>null</c> when no template has been configured.
    /// </summary>
    string? GetDailyTemplate(string vaultPath);

    /// <summary>Persists the daily-note template for the given vault.</summary>
    void SaveDailyTemplate(string vaultPath, string? template);

    /// <summary>
    /// Returns whether the app should automatically open today's daily note on startup.
    /// Defaults to <c>true</c> when not configured.
    /// </summary>
    bool GetOpenDailyOnStartup(string vaultPath);

    /// <summary>Persists the open-daily-on-startup preference for the given vault.</summary>
    void SaveOpenDailyOnStartup(string vaultPath, bool value);
}
