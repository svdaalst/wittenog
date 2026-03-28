using WitteNog.Core.Events;

namespace WitteNog.Core.Tests.Events;

public class NoteChangedEventTests
{
    [Theory]
    [InlineData(NoteChangeType.Created)]
    [InlineData(NoteChangeType.Modified)]
    [InlineData(NoteChangeType.Deleted)]
    public void NoteChangedEvent_StoresChangeType(NoteChangeType changeType)
    {
        var evt = new NoteChangedEvent("/vault/note.md", changeType);

        Assert.Equal("/vault/note.md", evt.FilePath);
        Assert.Equal(changeType, evt.ChangeType);
    }

    [Fact]
    public void NoteChangedEvent_IsImmutable_ViaRecord()
    {
        var original = new NoteChangedEvent("/vault/a.md", NoteChangeType.Created);
        var modified = original with { ChangeType = NoteChangeType.Deleted };

        Assert.Equal(NoteChangeType.Created, original.ChangeType);
        Assert.Equal(NoteChangeType.Deleted, modified.ChangeType);
    }
}
