namespace WitteNog.Infrastructure.Audio;

using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WitteNog.Core.Interfaces;

public sealed class AudioRecorderService : IAudioRecorder
{
    private const int TargetSampleRate = 16_000;

    // Microphone
    private WaveInEvent? _micCapture;
    private WaveFileWriter? _micWriter;
    private string? _micTempPath;

    // System audio (loopback) — optional, best-effort
    private WasapiLoopbackCapture? _loopbackCapture;
    private WaveFileWriter? _loopbackWriter;
    private string? _loopbackTempPath;

    // Final output
    private string? _outputFilePath;
    private bool _disposed;

    public bool IsRecording { get; private set; }

    public Task StartAsync(string outputDirectory, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsRecording)
            throw new InvalidOperationException(
                "Er loopt al een opname. Stop de huidige opname eerst.");

        Directory.CreateDirectory(outputDirectory);
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        _outputFilePath = Path.Combine(outputDirectory, $"recording-{stamp}.wav");

        var tempDir = Path.Combine(Path.GetTempPath(), "WitteNog");
        Directory.CreateDirectory(tempDir);
        _micTempPath      = Path.Combine(tempDir, $"mic-{stamp}.wav");
        _loopbackTempPath = Path.Combine(tempDir, $"loopback-{stamp}.wav");

        // ── Microfoon (16 kHz mono 16-bit PCM — optimaal voor Whisper) ──────────
        var micFormat = new WaveFormat(TargetSampleRate, 16, 1);
        _micCapture = new WaveInEvent { WaveFormat = micFormat };
        _micWriter  = new WaveFileWriter(_micTempPath, micFormat);
        _micCapture.DataAvailable += (_, e) => _micWriter?.Write(e.Buffer, 0, e.BytesRecorded);

        // ── OS-audio loopback (best-effort: stil overgeslagen als niet beschikbaar) ──
        try
        {
            _loopbackCapture = new WasapiLoopbackCapture();
            _loopbackWriter  = new WaveFileWriter(_loopbackTempPath, _loopbackCapture.WaveFormat);
            _loopbackCapture.DataAvailable += (_, e) =>
                _loopbackWriter?.Write(e.Buffer, 0, e.BytesRecorded);
            _loopbackCapture.StartRecording();
        }
        catch (Exception)
        {
            // Geen audio-uitvoerapparaat of WASAPI niet beschikbaar — alleen microfoon gebruiken
            _loopbackCapture?.Dispose(); _loopbackCapture = null;
            _loopbackWriter?.Dispose();  _loopbackWriter  = null;
        }

        try
        {
            _micCapture.StartRecording();
        }
        catch (NAudio.MmException ex)
        {
            Cleanup();
            throw new UnauthorizedAccessException(
                "Geen microfoon gevonden of toegang geweigerd.", ex);
        }

        IsRecording = true;
        return Task.CompletedTask;
    }

    public async Task<string> StopAsync(CancellationToken ct = default)
    {
        if (!IsRecording || _micCapture is null || _outputFilePath is null)
            throw new InvalidOperationException("Er is geen actieve opname.");

        // ── 1. Registreer completion sources VOOR StopRecording zodat we geen event missen ──
        var micStopped = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _micCapture.RecordingStopped += (_, _) => micStopped.TrySetResult(true);
        _micCapture.StopRecording();

        Task loopbackStopTask = Task.CompletedTask;
        if (_loopbackCapture is not null)
        {
            var loopStopped = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _loopbackCapture.RecordingStopped += (_, _) => loopStopped.TrySetResult(true);
            _loopbackCapture.StopRecording();
            loopbackStopTask = loopStopped.Task;
        }

        // ── 2. Wacht tot alle hardware-buffers zijn geleegd (max 5 s) ──────────────────
        await Task.WhenAny(
            Task.WhenAll(micStopped.Task, loopbackStopTask),
            Task.Delay(TimeSpan.FromSeconds(5), ct));

        // ── 3. Nu veilig: geen DataAvailable-events meer — flush en sluit writers ──────
        _micWriter?.Flush();      _micWriter?.Dispose();      _micWriter      = null;
        _loopbackWriter?.Flush(); _loopbackWriter?.Dispose(); _loopbackWriter = null;

        _micCapture.Dispose();         _micCapture      = null;
        _loopbackCapture?.Dispose();   _loopbackCapture = null;

        IsRecording = false;

        // ── 4. Meng microfoon + loopback → definitief uitvoerbestand ─────────────────
        await Task.Run(
            () => MixToOutput(_micTempPath!, _loopbackTempPath, _outputFilePath), ct);

        TryDelete(_micTempPath);
        TryDelete(_loopbackTempPath);

        return _outputFilePath;
    }

    /// <summary>
    /// Mengt microfoon- en loopback-streams naar één 16 kHz mono 16-bit PCM WAV.
    /// Als er geen loopbackbestand is (opname mislukt of geen audio afgespeeld),
    /// wordt alleen de microfoon gebruikt.
    /// </summary>
    private static void MixToOutput(
        string micPath, string? loopbackPath, string outputPath)
    {
        using var micReader = new AudioFileReader(micPath);
        var micSamples = new WdlResamplingSampleProvider(
            ToMono(micReader), TargetSampleRate);

        var hasLoopback = loopbackPath is not null
            && File.Exists(loopbackPath)
            && new FileInfo(loopbackPath).Length > 44; // meer dan alleen de WAV-header

        if (!hasLoopback)
        {
            WaveFileWriter.CreateWaveFile16(outputPath, micSamples);
            return;
        }

        using var loopReader = new AudioFileReader(loopbackPath!);
        var loopSamples = new WdlResamplingSampleProvider(
            ToMono(loopReader), TargetSampleRate);

        // FiniteMixingSampleProvider adds samples correctly (zeroes buffer first like ReadFully=true)
        // but returns 0 when ALL inputs are exhausted (unlike ReadFully=true which loops forever).
        var mixer = new FiniteMixingSampleProvider(
            WaveFormat.CreateIeeeFloatWaveFormat(TargetSampleRate, 1));
        mixer.AddInput(micSamples);
        mixer.AddInput(loopSamples);

        WaveFileWriter.CreateWaveFile16(outputPath, mixer);
    }

    private static ISampleProvider ToMono(AudioFileReader reader) =>
        reader.WaveFormat.Channels > 1
            ? new StereoToMonoSampleProvider(reader)
            : (ISampleProvider)reader;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (IsRecording)
            try { await StopAsync(); } catch { /* best effort bij afsluiten */ }
        Cleanup();
    }

    private void Cleanup()
    {
        _micCapture?.Dispose();      _micCapture      = null;
        _loopbackCapture?.Dispose(); _loopbackCapture = null;
        _micWriter?.Dispose();       _micWriter       = null;
        _loopbackWriter?.Dispose();  _loopbackWriter  = null;
        IsRecording = false;
    }

    private static void TryDelete(string? path)
    {
        if (path is not null)
            try { File.Delete(path); } catch { /* non-fatal */ }
    }

    /// <summary>
    /// Mixes meerdere <see cref="ISampleProvider"/> streams door samples op te tellen
    /// (buffer wordt eerst genulld zodat += correct werkt) en geeft 0 terug zodra ALLE
    /// inputs zijn uitgeput. Hiermee wordt de infinite-write-loop van
    /// <c>MixingSampleProvider.ReadFully = true</c> vermeden.
    /// </summary>
    private sealed class FiniteMixingSampleProvider : ISampleProvider
    {
        private readonly List<ISampleProvider> _inputs = [];
        private float[] _mixBuffer = [];

        public FiniteMixingSampleProvider(WaveFormat format) => WaveFormat = format;

        public WaveFormat WaveFormat { get; }

        public void AddInput(ISampleProvider input) => _inputs.Add(input);

        public int Read(float[] buffer, int offset, int count)
        {
            // Zero the output buffer so that each input can safely ADD its samples.
            Array.Clear(buffer, offset, count);

            if (_mixBuffer.Length < count)
                _mixBuffer = new float[count];

            int maxRead = 0;

            for (int i = _inputs.Count - 1; i >= 0; i--)
            {
                int samplesRead = _inputs[i].Read(_mixBuffer, 0, count);

                for (int n = 0; n < samplesRead; n++)
                    buffer[offset + n] += _mixBuffer[n]; // ADD into zeroed buffer

                if (samplesRead > maxRead) maxRead = samplesRead;

                if (samplesRead == 0)
                    _inputs.RemoveAt(i); // Remove exhausted input
            }

            // Returns 0 only when ALL inputs are exhausted → WaveFileWriter loop stops.
            return maxRead;
        }
    }
}
