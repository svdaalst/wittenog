namespace WitteNog.Application.Commands;

using MediatR;
using WitteNog.Core.Interfaces;

public record DeleteDrawingCommand(string FilePath) : IRequest;

public class DeleteDrawingCommandHandler : IRequestHandler<DeleteDrawingCommand>
{
    private readonly IDrawingRepository _repo;

    public DeleteDrawingCommandHandler(IDrawingRepository repo) => _repo = repo;

    public Task Handle(DeleteDrawingCommand request, CancellationToken ct)
        => _repo.DeleteAsync(request.FilePath, ct);
}
