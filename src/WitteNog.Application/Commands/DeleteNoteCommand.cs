namespace WitteNog.Application.Commands;

using MediatR;
using WitteNog.Core.Interfaces;

public record DeleteNoteCommand(string FilePath) : IRequest;

public class DeleteNoteCommandHandler : IRequestHandler<DeleteNoteCommand>
{
    private readonly IMarkdownStorage _storage;

    public DeleteNoteCommandHandler(IMarkdownStorage storage) => _storage = storage;

    public Task Handle(DeleteNoteCommand request, CancellationToken ct)
        => _storage.DeleteAsync(request.FilePath, ct);
}
