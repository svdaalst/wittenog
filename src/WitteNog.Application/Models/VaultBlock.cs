namespace WitteNog.Application.Models;

using WitteNog.Core.Models;

/// <summary>
/// Discriminated-union wrapper for items rendered on a virtual canvas page.
/// Exactly one of <see cref="Note"/>, <see cref="Flow"/>, or <see cref="Drawing"/> is non-null.
/// </summary>
public sealed class VaultBlock
{
    public AtomicNote? Note { get; }
    public FlowDiagram? Flow { get; }
    public Drawing? Drawing { get; }
    public string Id { get; }
    public DateTimeOffset LastModified { get; }
    public bool IsNote => Note is not null;
    public bool IsFlow => Flow is not null;
    public bool IsDrawing => Drawing is not null;

    private VaultBlock(AtomicNote? note, FlowDiagram? flow, Drawing? drawing, string id, DateTimeOffset lastModified)
    {
        Note = note;
        Flow = flow;
        Drawing = drawing;
        Id = id;
        LastModified = lastModified;
    }

    public static VaultBlock FromNote(AtomicNote note) => new(note, null, null, note.Id, note.LastModified);
    public static VaultBlock FromFlow(FlowDiagram flow) => new(null, flow, null, flow.Id, flow.LastModified);
    public static VaultBlock FromDrawing(Drawing drawing) => new(null, null, drawing, drawing.Id, drawing.LastModified);
}
