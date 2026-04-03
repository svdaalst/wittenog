namespace WitteNog.Application.Commands;

using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record SaveFlowCommand(FlowDiagram Diagram) : IRequest;

public class SaveFlowCommandHandler : IRequestHandler<SaveFlowCommand>
{
    private readonly IFlowRepository _repo;

    public SaveFlowCommandHandler(IFlowRepository repo) => _repo = repo;

    public Task Handle(SaveFlowCommand request, CancellationToken ct)
        => _repo.WriteAsync(request.Diagram, ct);
}
