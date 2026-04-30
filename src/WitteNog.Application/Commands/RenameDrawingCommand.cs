namespace WitteNog.Application.Commands;

using System.Text.RegularExpressions;
using MediatR;
using WitteNog.Core.Interfaces;
using WitteNog.Core.Models;

public record RenameDrawingCommand(Drawing Drawing, string NewStem) : IRequest<Drawing>;

public class RenameDrawingCommandHandler : IRequestHandler<RenameDrawingCommand, Drawing>
{
    private static readonly Regex WikiLinkTokenRegex =
        new(@"\[\[[^\]]+\]\]", RegexOptions.Compiled);

    private readonly IDrawingRepository _repo;
    private readonly IWikiLinkParser    _linkParser;

    public RenameDrawingCommandHandler(IDrawingRepository repo, IWikiLinkParser linkParser)
    {
        _repo       = repo;
        _linkParser = linkParser;
    }

    public async Task<Drawing> Handle(RenameDrawingCommand request, CancellationToken ct)
    {
        var stem = request.NewStem.Trim();
        ValidateStem(stem);

        var wikiLinks = _linkParser.ExtractLinks(stem);
        var title     = WikiLinkTokenRegex.Replace(stem, "").Trim();

        var updated = request.Drawing with
        {
            WikiLinks    = wikiLinks,
            Title        = title,
            LastModified = DateTimeOffset.UtcNow,
        };

        await _repo.WriteAsync(updated, ct);
        return updated;
    }

    /// <summary>
    /// Public so <see cref="WitteNog.App.Components.DrawingBlock"/> can pre-validate
    /// before sending the command (same pattern as <see cref="RenameNoteCommandHandler.ValidateSlug"/>).
    /// </summary>
    public static void ValidateStem(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
            throw new ArgumentException("Naam mag niet leeg zijn.");

        // Strip [[...]] tokens before checking for illegal filename characters
        var title  = WikiLinkTokenRegex.Replace(stem, "").Trim();
        var illegal = new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
        if (title.IndexOfAny(illegal) >= 0)
            throw new ArgumentException("Naam bevat ongeldige tekens (\\ / : * ? \" < > |).");

        if (stem.Contains(".."))
            throw new ArgumentException("Naam mag geen '..' bevatten.");

        if (stem.Length > 200)
            throw new ArgumentException("Naam mag maximaal 200 tekens bevatten.");
    }
}
