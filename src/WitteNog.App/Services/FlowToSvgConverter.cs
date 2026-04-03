namespace WitteNog.App.Services;

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using WitteNog.Core.Models;

/// <summary>
/// Converts a <see cref="FlowDiagram"/> to an inline SVG string.
/// Analogous to <c>MarkdownRenderer</c> — stateless, no DI required.
///
/// Styles are embedded directly inside the SVG (not via CSS classes) because
/// CSS custom properties (var(--accent) etc.) do not reliably resolve on SVG
/// elements injected as a MarkupString inside MAUI's BlazorWebView.
/// WikiLink tspans still carry <c>data-wikilink</c> and <c>class="flow-wikilink"</c>
/// so FlowBlockDelegate can intercept clicks.
/// </summary>
public static class FlowToSvgConverter
{
    // Design tokens — kept in sync with app.css variables.
    private const string ColBg       = "#16213e";   // --surface
    private const string ColSurface2 = "#0f3460";   // --surface-2
    private const string ColAccent   = "#e94560";   // --accent
    private const string ColText     = "#eaeaea";   // --text
    private const string ColMuted    = "#8892a4";   // --text-muted

    private const double Padding    = 24.0;
    private const double LineHeight = 18.0;

    private static readonly Regex WikiLinkRegex =
        new(@"\[\[([^\]]+)\]\]", RegexOptions.Compiled);

    // ── Public API ───────────────────────────────────────────────────────────

    public static string Convert(FlowDiagram diagram)
    {
        if (diagram.Nodes.Count == 0)
            return RenderEmpty();

        var (minX, minY, maxX, maxY) = ComputeBoundingBox(diagram.Nodes);
        var vbX = minX - Padding;
        var vbY = minY - Padding;
        var vbW = maxX - minX + Padding * 2;
        var vbH = maxY - minY + Padding * 2;

        var markerId = SanitizeId(diagram.Id);
        var sb = new StringBuilder();

        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\"");
        sb.Append($" viewBox=\"{vbX:F0} {vbY:F0} {vbW:F0} {vbH:F0}\"");
        sb.Append($" width=\"100%\" overflow=\"visible\"");
        sb.Append($" class=\"flow-diagram\">");

        // Embedded styles — avoids CSS-variable resolution failure inside MarkupString
        sb.Append("<style>");
        sb.Append($".fns{{fill:{ColSurface2};stroke:{ColAccent};stroke-width:1.5;}}");
        sb.Append($".fnt{{fill:{ColText};font-size:13px;font-family:sans-serif;dominant-baseline:middle;}}");
        sb.Append($".fwl{{fill:{ColAccent};cursor:pointer;text-decoration:underline;}}");
        sb.Append($".fed{{fill:none;stroke:{ColMuted};stroke-width:1.5;}}");
        sb.Append($".fel{{fill:{ColMuted};font-size:11px;font-family:sans-serif;}}");
        sb.Append($".fah{{fill:{ColMuted};}}");
        sb.Append($".fem{{fill:{ColMuted};font-size:13px;font-family:sans-serif;}}");
        sb.Append("</style>");

        // Background rect so the diagram always has a visible area
        sb.Append($"<rect x=\"{vbX:F0}\" y=\"{vbY:F0}\" width=\"{vbW:F0}\" height=\"{vbH:F0}\" fill=\"{ColBg}\" rx=\"6\"/>");

        // Arrow-head marker
        sb.Append("<defs>");
        sb.Append($"<marker id=\"a{markerId}\" markerWidth=\"10\" markerHeight=\"7\" refX=\"9\" refY=\"3.5\" orient=\"auto\">");
        sb.Append($"<polygon points=\"0 0,10 3.5,0 7\" class=\"fah\"/>");
        sb.Append("</marker>");
        sb.Append("</defs>");

        // Edges first (below nodes)
        var nodeMap = diagram.Nodes.ToDictionary(n => n.Id);
        foreach (var edge in diagram.Edges)
        {
            if (!nodeMap.TryGetValue(edge.FromNodeId, out var from)) continue;
            if (!nodeMap.TryGetValue(edge.ToNodeId, out var to)) continue;
            sb.Append(RenderEdge(edge, from, to, markerId));
        }

        // Nodes on top
        foreach (var node in diagram.Nodes)
            sb.Append(RenderNode(node));

        sb.Append("</svg>");
        return sb.ToString();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string RenderEmpty() =>
        $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 300 80\" width=\"100%\"" +
        $" class=\"flow-diagram flow-diagram-empty\">" +
        $"<rect width=\"300\" height=\"80\" fill=\"{ColBg}\" rx=\"6\"/>" +
        $"<style>.fem{{fill:{ColMuted};font-size:13px;font-family:sans-serif;}}</style>" +
        $"<text x=\"150\" y=\"40\" text-anchor=\"middle\" dominant-baseline=\"middle\" class=\"fem\">Leeg diagram — klik ✏️ om te bewerken</text>" +
        $"</svg>";

    private static string RenderNode(FlowNode node)
    {
        var sb = new StringBuilder();
        sb.Append($"<g data-node-id=\"{WebUtility.HtmlEncode(node.Id)}\">");
        sb.Append(RenderShape(node));
        sb.Append(RenderNodeText(node));
        sb.Append("</g>");
        return sb.ToString();
    }

    private static string RenderShape(FlowNode node)
    {
        var x  = node.X;
        var y  = node.Y;
        var w  = node.Width;
        var h  = node.Height;
        var cx = x + w / 2;
        var cy = y + h / 2;

        return node.Shape switch
        {
            NodeShape.Diamond =>
                $"<polygon points=\"{cx:F1},{y:F1} {x+w:F1},{cy:F1} {cx:F1},{y+h:F1} {x:F1},{cy:F1}\" class=\"fns\"/>",
            NodeShape.Ellipse =>
                $"<ellipse cx=\"{cx:F1}\" cy=\"{cy:F1}\" rx=\"{w/2:F1}\" ry=\"{h/2:F1}\" class=\"fns\"/>",
            _ =>
                $"<rect x=\"{x:F1}\" y=\"{y:F1}\" width=\"{w:F1}\" height=\"{h:F1}\" rx=\"4\" class=\"fns\"/>",
        };
    }

    private static string RenderNodeText(FlowNode node)
    {
        var cx     = node.X + node.Width  / 2;
        var cy     = node.Y + node.Height / 2;
        var lines  = node.Text.Split('\n');
        var total  = lines.Length * LineHeight;
        // First line baseline: vertically centre the whole block
        var startY = cy - total / 2 + LineHeight * 0.5;

        var sb = new StringBuilder();
        sb.Append($"<text text-anchor=\"middle\" class=\"fnt\">");
        for (var i = 0; i < lines.Length; i++)
        {
            var y = startY + i * LineHeight;
            sb.Append($"<tspan x=\"{cx:F1}\" y=\"{y:F1}\">{RenderTextSegments(lines[i])}</tspan>");
        }
        sb.Append("</text>");
        return sb.ToString();
    }

    private static string RenderTextSegments(string text)
    {
        var result    = new StringBuilder();
        var lastIndex = 0;

        foreach (Match match in WikiLinkRegex.Matches(text))
        {
            if (match.Index > lastIndex)
                result.Append(WebUtility.HtmlEncode(text[lastIndex..match.Index]));

            var link = match.Groups[1].Value;
            var enc  = WebUtility.HtmlEncode(link);
            result.Append($"<tspan data-wikilink=\"{enc}\" class=\"flow-wikilink fwl\">[[{enc}]]</tspan>");
            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
            result.Append(WebUtility.HtmlEncode(text[lastIndex..]));

        return result.ToString();
    }

    private static string RenderEdge(FlowEdge edge, FlowNode from, FlowNode to, string markerId)
    {
        var x1   = from.X + from.Width  / 2;
        var y1   = from.Y + from.Height / 2;
        var x2   = to.X   + to.Width    / 2;
        var y2   = to.Y   + to.Height   / 2;
        var midX = (x1 + x2) / 2;
        var midY = (y1 + y2) / 2 - 20;

        var sb = new StringBuilder();
        sb.Append($"<path d=\"M {x1:F1} {y1:F1} Q {midX:F1} {midY:F1} {x2:F1} {y2:F1}\"");
        sb.Append($" class=\"fed\" marker-end=\"url(#a{markerId})\"/>");

        if (!string.IsNullOrWhiteSpace(edge.Label))
        {
            sb.Append($"<text x=\"{midX:F1}\" y=\"{midY - 4:F1}\" text-anchor=\"middle\" class=\"fel\">");
            sb.Append(WebUtility.HtmlEncode(edge.Label));
            sb.Append("</text>");
        }

        return sb.ToString();
    }

    private static (double minX, double minY, double maxX, double maxY) ComputeBoundingBox(
        IReadOnlyList<FlowNode> nodes)
    {
        var minX = nodes.Min(n => n.X);
        var minY = nodes.Min(n => n.Y);
        var maxX = nodes.Max(n => n.X + n.Width);
        var maxY = nodes.Max(n => n.Y + n.Height);
        return (minX, minY, maxX, maxY);
    }

    private static string SanitizeId(string id) =>
        Regex.Replace(id, @"[^\w]", "_");
}
