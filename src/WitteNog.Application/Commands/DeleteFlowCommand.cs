namespace WitteNog.Application.Commands;

using MediatR;
using WitteNog.Core.Interfaces;

public record DeleteFlowCommand(string FilePath) : IRequest;

public class DeleteFlowCommandHandler : IRequestHandler<DeleteFlowCommand>
{
    private readonly IFlowRepository _repo;

    public DeleteFlowCommandHandler(IFlowRepository repo) => _repo = repo;

    public Task Handle(DeleteFlowCommand request, CancellationToken ct)
        => _repo.DeleteAsync(request.FilePath, ct);
}
