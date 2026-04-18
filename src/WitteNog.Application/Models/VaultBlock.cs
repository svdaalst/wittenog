namespace WitteNog.Application.Models;

using WitteNog.Core.Models;

/// <summary>
/// Discriminated-union wrapper for items rendered on a virtual canvas page.
/// Exactly one of <see cref="Note"/> or <see cref="Flow"/> is non-null.
/// </summary>
public sealed class VaultBlock
{
    public AtomicNote? Note { get; }
    public FlowDiagram? Flow { get; }
    public string Id { get; }
    public DateTimeOffset LastModified { get; }
    public bool IsNote => Note is not null;
    public bool IsFlow => Flow is not null;

    private VaultBlock(AtomicNote? note, FlowDiagram? flow, string id, DateTimeOffset lastModified)
    {
        Note = note;
        Flow = flow;
        Id = id;
        LastModified = lastModified;
    }

    public static VaultBlock FromNote(AtomicNote note) => new(note, null, note.Id, note.LastModified);
    public static VaultBlock FromFlow(FlowDiagram flow) => new(null, flow, flow.Id, flow.LastModified);
}
