namespace WitteNog.Core.Interfaces;

public interface ITranscriptionService
{
    /// <summary>Returns true when the Whisper model file is present on disk.</summary>
    bool IsModelReady { get; }

    /// <summary>
    /// The Whisper model to use: "Tiny" | "Base" | "Small" | "Medium" | "Large".
    /// Set this before calling <see cref="EnsureModelAsync"/> so the correct model is downloaded.
    /// Defaults to "Base". Changing this after the factory has loaded causes a factory reload.
    /// </summary>
    string ConfiguredModel { get; set; }

    /// <summary>
    /// Downloads/verifies the configured Whisper model if not already present.
    /// Reports download progress via <paramref name="progress"/> (0.0–1.0).
    /// </summary>
    Task EnsureModelAsync(IProgress<double>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Transcribes the WAV file at <paramref name="wavFilePath"/> in the given ISO 639-1
    /// <paramref name="language"/> (e.g. "nl", "en") and returns the recognised text.
    /// Pass "auto" for automatic language detection.
    /// Returns <see cref="string.Empty"/> when nothing was recognised.
    /// </summary>
    Task<string> TranscribeAsync(
        string wavFilePath, string language, CancellationToken ct = default);
}
