namespace WitteNog.Application.Commands;

using System.IO.Abstractions;
using System.Text.RegularExpressions;
using MediatR;
using WitteNog.Core.Interfaces;

/// <summary>
/// Deletes a note. When <paramref name="VaultPath"/> is provided, also deletes any
/// "attachments/..." files referenced ONLY by the deleted note (M8 — privacy: clipboard
/// screenshots no longer linger after the user deletes the note that contained them).
/// VaultPath is optional so callers that don't care about attachment GC stay terse;
/// existing tests construct the command without it.
/// </summary>
public record DeleteNoteCommand(string FilePath, string? VaultPath = null) : IRequest;

public class DeleteNoteCommandHandler : IRequestHandler<DeleteNoteCommand>
{
    // Matches `![alt](attachments/something.ext)` — captures the relative path so we can
    // both compare it against other notes and reconstruct the absolute path for deletion.
    // Forward-slash only because the markdown is what the user types/pastes.
    private static readonly Regex AttachmentRegex =
        new(@"!\[[^\]]*\]\((attachments/[^)]+)\)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IMarkdownStorage _storage;
    private readonly IFileSystem _fs;

    public DeleteNoteCommandHandler(IMarkdownStorage storage, IFileSystem fs)
    {
        _storage = storage;
        _fs = fs;
    }

    public async Task Handle(DeleteNoteCommand request, CancellationToken ct)
    {
        // Skip-GC fast path: caller didn't pass a vault root → just delete and exit.
        // Keeps the older 1-arg overload (used by existing tests) byte-for-byte
        // backward-compatible.
        if (string.IsNullOrEmpty(request.VaultPath))
        {
            await _storage.DeleteAsync(request.FilePath, ct);
            return;
        }

        // Read the note BEFORE deleting so we can extract its attachment refs.
        var deletedNote = await _storage.ReadAsync(request.FilePath, ct);
        var attachmentsToCheck = ExtractAttachmentReferences(deletedNote?.Content);

        await _storage.DeleteAsync(request.FilePath, ct);

        if (attachmentsToCheck.Count == 0) return;

        // Walk every remaining note in the vault to build the "still referenced" set.
        // This is O(notes × attachments-in-deleted) which is fine for typical vaults
        // (hundreds of notes). For massive vaults a future optimisation could index
        // attachment references in the cache.
        var stillReferenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var note in _storage.ReadAllAsync(request.VaultPath!, ct))
        {
            foreach (var att in ExtractAttachmentReferences(note.Content))
            {
                stillReferenced.Add(att);
                // Early exit: once every candidate is accounted for, stop scanning.
                if (stillReferenced.IsSupersetOf(attachmentsToCheck)) goto delete;
            }
        }

        delete:
        foreach (var attRel in attachmentsToCheck)
        {
            if (stillReferenced.Contains(attRel)) continue;

            // The reference is "attachments/foo.png" relative to the vault root.
            // Path.GetFullPath + StartsWith check is defensive — a future markdown
            // construct that puts ".." in the path must not let a deletion escape
            // the vault root.
            var vaultRoot = _fs.Path.GetFullPath(request.VaultPath!);
            var absPath = _fs.Path.GetFullPath(_fs.Path.Combine(vaultRoot, attRel));
            if (!absPath.StartsWith(
                    vaultRoot.TrimEnd(_fs.Path.DirectorySeparatorChar, _fs.Path.AltDirectorySeparatorChar)
                    + _fs.Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                if (_fs.File.Exists(absPath))
                    _fs.File.Delete(absPath);
            }
            catch (Exception ex)
            {
                // Soft-fail: a stuck file or a permissions issue mustn't make the
                // delete itself fail (the note is already gone). Log and move on.
                Console.Error.WriteLine($"Attachment GC failed for {absPath}: {ex.Message}");
            }
        }
    }

    private static IReadOnlyCollection<string> ExtractAttachmentReferences(string? content)
    {
        if (string.IsNullOrEmpty(content)) return Array.Empty<string>();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AttachmentRegex.Matches(content))
            set.Add(m.Groups[1].Value);
        return set;
    }
}
