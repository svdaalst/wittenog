namespace WitteNog.Application.Commands;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record SaveDrawingCommand(Drawing Drawing) : IRequest;

public class SaveDrawingCommandHandler : IRequestHandler<SaveDrawingCommand>
{
    private readonly IDrawingRepository _repo;

    public SaveDrawingCommandHandler(IDrawingRepository repo) => _repo = repo;

    public Task Handle(SaveDrawingCommand request, CancellationToken ct)
        => _repo.WriteAsync(request.Drawing, ct);
}
