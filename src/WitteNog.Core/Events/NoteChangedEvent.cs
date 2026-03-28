namespace WitteNog.Core.Events;

public record NoteChangedEvent(string FilePath, NoteChangeType ChangeType);

public enum NoteChangeType { Created, Modified, Deleted }
