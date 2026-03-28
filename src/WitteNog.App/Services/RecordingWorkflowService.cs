namespace WitteNog.App.Services;

using MediatR;
using WitteNog.Application.Commands;
using WitteNog.Core.Interfaces;

/// <summary>
/// Singleton that owns the full record → transcribe → append workflow.
/// Components subscribe to <see cref="StateChanged"/> to stay in sync after re-renders.
/// </summary>
public class RecordingWorkflowService : IAsyncDisposable
{
    public enum WorkflowState { Idle, DownloadingModel, Recording, WaitingForLanguage, Transcribing }

    private readonly IAudioRecorder _recorder;
    private readonly ITranscriptionService _transcription;
    private readonly IMediator _mediator;
    private readonly VaultContextService _vault;
    private readonly IVaultSettings _vaultSettings;

    public WorkflowState State { get; private set; } = WorkflowState.Idle;
    public double DownloadProgress { get; private set; }
    public string? RecordingForFilePath { get; private set; }
    public string? PendingWavPath { get; private set; }
    public string? LastError { get; private set; }

    public event Action? StateChanged;

    public RecordingWorkflowService(
        IAudioRecorder recorder,
        ITranscriptionService transcription,
        IMediator mediator,
        VaultContextService vault,
        IVaultSettings vaultSettings)
    {
        _recorder = recorder;
        _transcription = transcription;
        _mediator = mediator;
        _vault = vault;
        _vaultSettings = vaultSettings;
    }

    public async Task StartAsync(string noteFilePath, CancellationToken ct = default)
    {
        if (State != WorkflowState.Idle)
            throw new InvalidOperationException(
                "Er loopt al een opname. Stop de huidige opname eerst.");

        LastError = null;

        // Apply model from vault settings before ensuring the model is ready.
        if (_vault.VaultPath is not null)
        {
            var ts = _vaultSettings.GetTranscriptionSettings(_vault.VaultPath);
            _transcription.ConfiguredModel = ts.Model;
        }

        if (!_transcription.IsModelReady)
        {
            State = WorkflowState.DownloadingModel;
            DownloadProgress = 0;
            StateChanged?.Invoke();

            var progress = new Progress<double>(p =>
            {
                DownloadProgress = p;
                StateChanged?.Invoke();
            });
            await _transcription.EnsureModelAsync(progress, ct);
        }

        var recordingsDir = Path.Combine(
            _vault.VaultPath ?? Path.GetTempPath(), "recordings");
        await _recorder.StartAsync(recordingsDir, ct);
        RecordingForFilePath = noteFilePath;
        State = WorkflowState.Recording;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Stops the recording and transitions to <see cref="WorkflowState.WaitingForLanguage"/>.
    /// The WAV file is saved but transcription has not started yet.
    /// Call <see cref="ConfirmTranscriptionAsync"/> or <see cref="CancelTranscriptionAsync"/> next.
    /// </summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        if (State != WorkflowState.Recording)
            throw new InvalidOperationException("Er is geen actieve opname.");

        LastError = null;
        try
        {
            PendingWavPath = await _recorder.StopAsync(ct);
            State = WorkflowState.WaitingForLanguage;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            RecordingForFilePath = null;
            State = WorkflowState.Idle;
        }
        StateChanged?.Invoke();
    }

    /// <summary>Transcribes the pending WAV with the chosen language and appends the result to the note.</summary>
    public async Task ConfirmTranscriptionAsync(string language, CancellationToken ct = default)
    {
        if (State != WorkflowState.WaitingForLanguage || PendingWavPath is null)
            throw new InvalidOperationException("Geen opname klaar voor transcriptie.");

        State = WorkflowState.Transcribing;
        StateChanged?.Invoke();

        var filePath = RecordingForFilePath;
        var wavPath = PendingWavPath;
        PendingWavPath = null;

        try
        {
            var text = await _transcription.TranscribeAsync(wavPath, language, ct);

            if (string.IsNullOrWhiteSpace(text))
                LastError = "Geen spraak herkend. Probeer opnieuw.";
            else if (filePath is not null)
                await _mediator.Send(
                    new AppendTranscriptionCommand(filePath, text, DateTimeOffset.Now, wavPath), ct);
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }
        finally
        {
            RecordingForFilePath = null;
            State = WorkflowState.Idle;
            StateChanged?.Invoke();
        }
    }

    /// <summary>Discards the pending WAV and returns to Idle without transcribing.</summary>
    public Task CancelTranscriptionAsync()
    {
        if (State != WorkflowState.WaitingForLanguage)
            return Task.CompletedTask;

        if (PendingWavPath is not null)
            try { File.Delete(PendingWavPath); } catch { /* non-fatal */ }

        PendingWavPath = null;
        RecordingForFilePath = null;
        State = WorkflowState.Idle;
        StateChanged?.Invoke();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => _recorder.DisposeAsync();
}
