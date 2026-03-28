namespace WitteNog.App;

using Android.Media;
using WitteNog.Core.Interfaces;

/// <summary>
/// Android microphone recorder. Captures 16 kHz mono 16-bit PCM and writes a WAV file.
/// System audio loopback is not available on Android for third-party apps.
/// Compiled only for net9.0-android (file lives in Platforms/Android/).
/// </summary>
internal sealed class AndroidAudioRecorderService : IAudioRecorder
{
    private const int SampleRate = 16_000;
    private const ChannelIn Channel = ChannelIn.Mono;
    private const Android.Media.Encoding AudioFmt = Android.Media.Encoding.Pcm16bit;

    private AudioRecord? _audioRecord;
    private string? _outputFilePath;
    private Thread? _captureThread;
    private volatile bool _capturing;
    private bool _disposed;

    public bool IsRecording => _capturing;

    public async Task StartAsync(string outputDirectory, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_capturing)
            throw new InvalidOperationException("Er loopt al een opname. Stop de huidige opname eerst.");

        var status = await Permissions.RequestAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
            throw new UnauthorizedAccessException("Microfoon toegang geweigerd.");

        Directory.CreateDirectory(outputDirectory);
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        _outputFilePath = Path.Combine(outputDirectory, $"recording-{stamp}.wav");

        int minBuffer = AudioRecord.GetMinBufferSize(SampleRate, Channel, AudioFmt);
        _audioRecord = new AudioRecord(AudioSource.Mic, SampleRate, Channel, AudioFmt, minBuffer * 4);
        _audioRecord.StartRecording();
        _capturing = true;

        _captureThread = new Thread(() => CaptureLoop(_outputFilePath, minBuffer)) { IsBackground = true };
        _captureThread.Start();
    }

    public Task<string> StopAsync(CancellationToken ct = default)
    {
        if (!_capturing || _outputFilePath is null)
            throw new InvalidOperationException("Er is geen actieve opname.");

        _capturing = false;
        _audioRecord?.Stop();
        _captureThread?.Join(TimeSpan.FromSeconds(3));

        return Task.FromResult(_outputFilePath);
    }

    private void CaptureLoop(string outputPath, int bufferSize)
    {
        var pcmBuffer = new byte[bufferSize * 2];

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite);
        WriteWavHeader(fs, 0);
        long dataSize = 0;

        while (_capturing)
        {
            int bytesRead = _audioRecord?.Read(pcmBuffer, 0, pcmBuffer.Length) ?? 0;
            if (bytesRead > 0)
            {
                fs.Write(pcmBuffer, 0, bytesRead);
                dataSize += bytesRead;
            }
            else if (bytesRead < 0)
            {
                break; // AudioRecord stopped or error
            }
        }

        // Patch WAV header with actual data sizes
        fs.Seek(4, SeekOrigin.Begin);
        using var w = new BinaryWriter(fs, System.Text.Encoding.ASCII, leaveOpen: true);
        w.Write((int)(36 + dataSize));  // RIFF chunk size
        fs.Seek(40, SeekOrigin.Begin);
        w.Write((int)dataSize);         // data chunk size
    }

    /// <summary>Writes a 44-byte WAV header. Call with dataSize=0 initially; patch later.</summary>
    private static void WriteWavHeader(Stream s, int dataSize)
    {
        const int sampleRate = SampleRate;
        const short channels = 1;
        const short bitsPerSample = 16;
        int byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = (short)(channels * bitsPerSample / 8);

        using var w = new BinaryWriter(s, System.Text.Encoding.ASCII, leaveOpen: true);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataSize);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);               // fmt chunk size
        w.Write((short)1);         // PCM
        w.Write(channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write(blockAlign);
        w.Write(bitsPerSample);
        w.Write("data"u8.ToArray());
        w.Write(dataSize);
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;
        if (_capturing)
        {
            _capturing = false;
            _audioRecord?.Stop();
            _captureThread?.Join(TimeSpan.FromSeconds(2));
        }
        _audioRecord?.Release();
        _audioRecord = null;
        return ValueTask.CompletedTask;
    }
}
