namespace WitteNog.Core.Interfaces;

public interface ILinkMetadataService
{
    bool IsArchived(string vaultPath, string link);
    void SetArchivedStatus(string vaultPath, string link, bool archived);
    IReadOnlySet<string> GetArchivedLinks(string vaultPath);

    /// <summary>Fired after SetArchivedStatus; subscribers can refresh UI.</summary>
    event Action? MetadataChanged;

    /// <summary>Fired when archiving a link that still has open tasks. Argument is the link name.</summary>
    event Action<string>? ArchiveGuardTriggered;

    /// <summary>Clears the in-memory cache so the next read reloads from disk.
    /// Call this when an external sync tool changes vault-settings.json on disk.</summary>
    void InvalidateCache(string vaultPath);
}
