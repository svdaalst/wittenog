namespace WitteNog.Infrastructure.Audio;

using System.Text;
using Whisper.net;
using Whisper.net.Ggml;
using WitteNog.Core.Interfaces;

public sealed class WhisperTranscriptionService : ITranscriptionService, IAsyncDisposable
{
    private readonly string ModelDir;

    public WhisperTranscriptionService(string modelDirectory)
    {
        ModelDir = modelDirectory;
    }

    private string _configuredModel = "Base";
    private GgmlType _ggmlType = GgmlType.Base;

    // Lazy-loaded; reused across calls. Disposed when ConfiguredModel changes.
    private WhisperFactory? _factory;
    private bool _disposed;

    public string ConfiguredModel
    {
        get => _configuredModel;
        set
        {
            if (_configuredModel == value) return;
            _configuredModel = value;
            _ggmlType = ParseGgmlType(value);
            // Invalidate factory so it reloads with the new model on next use
            _factory?.Dispose();
            _factory = null;
        }
    }

    private string ModelPath =>
        Path.Combine(ModelDir, $"ggml-{_ggmlType.ToString().ToLowerInvariant()}.bin");

    private string ModelTmpPath => ModelPath + ".tmp";

    public bool IsModelReady => File.Exists(ModelPath);

    public async Task EnsureModelAsync(
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (IsModelReady) return;

        Directory.CreateDirectory(ModelDir);

        if (File.Exists(ModelTmpPath))
            File.Delete(ModelTmpPath);

        using var modelStream = await WhisperGgmlDownloader
            .GetGgmlModelAsync(_ggmlType, QuantizationType.NoQuantization, ct);

        long? totalBytes = modelStream.CanSeek ? modelStream.Length : null;
        long bytesRead = 0;

        await using (var fs = File.Create(ModelTmpPath))
        {
            var buffer = new byte[81_920];
            int read;
            while ((read = await modelStream.ReadAsync(buffer, ct)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                bytesRead += read;
                if (progress is not null && totalBytes.HasValue)
                    progress.Report((double)bytesRead / totalBytes.Value);
            }
        }

        // Atomic rename — only do this after a successful full download
        File.Move(ModelTmpPath, ModelPath, overwrite: false);
    }

    public async Task<string> TranscribeAsync(
        string wavFilePath, string language, CancellationToken ct = default)
    {
        if (!IsModelReady)
            throw new InvalidOperationException(
                "Whisper-model is niet beschikbaar. Roep EnsureModelAsync aan eerst.");

        _factory ??= WhisperFactory.FromPath(ModelPath);

        var builder = _factory.CreateBuilder();

        if (language == "auto")
            builder = builder.WithLanguageDetection();
        else
            builder = builder.WithLanguage(language);

        await using var processor = builder.Build();
        await using var fileStream = File.OpenRead(wavFilePath);
        var sb = new StringBuilder();

        await foreach (var segment in processor.ProcessAsync(fileStream, ct))
            sb.Append(segment.Text);

        return sb.ToString().Trim();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        _factory?.Dispose();
        _factory = null;
        return ValueTask.CompletedTask;
    }

    private static GgmlType ParseGgmlType(string model) =>
        Enum.TryParse<GgmlType>(model, ignoreCase: true, out var result) ? result : GgmlType.Base;
}
