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
}
