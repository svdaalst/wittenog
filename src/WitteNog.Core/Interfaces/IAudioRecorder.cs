namespace WitteNog.Core.Interfaces;

public interface IAudioRecorder : IAsyncDisposable
{
    bool IsRecording { get; }

    /// <summary>
    /// Starts capturing audio from the default microphone and writes a WAV file into
    /// <paramref name="outputDirectory"/>.
    /// Throws <see cref="InvalidOperationException"/> if already recording.
    /// Throws <see cref="UnauthorizedAccessException"/> if microphone access is denied.
    /// </summary>
    Task StartAsync(string outputDirectory, CancellationToken ct = default);

    /// <summary>
    /// Stops capture, flushes the WAV file, and returns its absolute path.
    /// The caller is responsible for deleting the file after use.
    /// </summary>
    Task<string> StopAsync(CancellationToken ct = default);
}
