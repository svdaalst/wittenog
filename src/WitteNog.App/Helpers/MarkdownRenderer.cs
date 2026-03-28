namespace WitteNog.App.Helpers;

using System.Text.RegularExpressions;
using Markdig;

public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static readonly Regex WikiLinkRegex =
        new(@"\[\[([^\]]+)\]\]", RegexOptions.Compiled);

    // Matches <a href="...audio-ext">label</a> produced by Markdig for local audio links.
    // Replaces them with a span so WebView2 never tries to navigate to the path as a URL.
    private static readonly Regex AudioLinkRegex =
        new(@"<a href=""([^""]*\.(?:wav|mp3|ogg|m4a|flac))""[^>]*>(.*?)</a>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Renders markdown to HTML. WikiLinks become clickable spans with data-wikilink attributes.
    /// Audio file links become spans with data-audiolink attributes (opened via the OS).
    /// </summary>
    public static string Render(string markdown)
    {
        // Replace [[link]] with a clickable span BEFORE Markdig processes the text
        var withLinks = WikiLinkRegex.Replace(markdown, m =>
        {
            var link = m.Groups[1].Value;
            var encoded = System.Net.WebUtility.HtmlEncode(link);
            return $"<span class=\"wiki-link\" data-wikilink=\"{encoded}\">{encoded}</span>";
        });

        var html = Markdig.Markdown.ToHtml(withLinks, Pipeline);

        // Convert audio <a href> tags to spans so WebView2 doesn't navigate to them
        return AudioLinkRegex.Replace(html, m =>
            $"<span class=\"audio-link\" data-audiolink=\"{m.Groups[1].Value}\">{m.Groups[2].Value}</span>");
    }
}
