namespace WitteNog.App.Services;

/// <summary>
/// Ensures at most one NoteBlock is in edit mode at a time.
/// When a second note requests edit mode, the first is auto-saved and closed.
/// </summary>
public sealed class EditModeCoordinatorService
{
    private Func<Task>? _pendingSave;

    public string? ActiveNoteId { get; private set; }

    public bool IsInEditMode => ActiveNoteId != null;

    /// <summary>
    /// Request edit mode for <paramref name="noteId"/>.
    /// If another note is currently editing, its save callback is awaited first.
    /// </summary>
    public async Task TryEnterEditModeAsync(string noteId, Func<Task> saveAndCloseCallback)
    {
        if (ActiveNoteId == noteId) return;

        if (_pendingSave != null)
            await _pendingSave();

        ActiveNoteId = noteId;
        _pendingSave = saveAndCloseCallback;
    }

    /// <summary>Called when a note exits edit mode (save, cancel, or dispose).</summary>
    public void ExitEditMode(string noteId)
    {
        if (ActiveNoteId == noteId)
        {
            ActiveNoteId = null;
            _pendingSave = null;
        }
    }
}
