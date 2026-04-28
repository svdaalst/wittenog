namespace WitteNog.App.Helpers;

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Markdig;

public static class MarkdownRenderer
{
    // We intentionally do NOT call .DisableHtml() here. We pre-inject our own HTML
    // (wiki-link spans and inline-task buttons) before Markdig runs, and DisableHtml
    // would escape that HTML — the user would see literal <button>...</button> text in
    // their notes instead of a clickable checkbox. XSS from a hostile note's <script>
    // is now blocked at execution time by the CSP in index.html (script-src 'self',
    // no 'unsafe-inline').
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

    // Matches relative <img src="..."> to rewrite to file:/// so WebView2 can load vault images.
    private static readonly Regex RelativeImgRegex =
        new(@"<img([^>]*) src=""(?!https?://|file://|data:)([^""]+)""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // H4: caches the (path → base64 data URI) result so we don't re-read + re-encode the
    // same image on every render. Key is the absolute path; value carries the file's
    // mtime so an external change (re-paste, edit, delete + recreate) invalidates the
    // entry on next access. Concurrent because Render() can be called from any thread.
    private static readonly ConcurrentDictionary<string, (DateTime Mtime, string DataUri)>
        _dataUriCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Renders markdown to HTML. WikiLinks become clickable spans with data-wikilink attributes.
    /// Audio file links become spans with data-audiolink attributes (opened via the OS).
    /// When filePath is provided, unchecked task checkboxes become clickable buttons.
    /// lineOffset must equal the number of lines stripped from the top of the original file
    /// (e.g. the H1 + trailing blank lines) so that task IDs reference the correct file line.
    /// When vaultRoot is provided, relative &lt;img&gt; sources are inlined as base64 data URIs
    /// only if the resolved absolute path is inside vaultRoot AND the extension is in the
    /// allowed image whitelist. This prevents a hostile .md from inlining arbitrary local
    /// files (e.g. ![](../../../Windows/win.ini)) into the DOM.
    /// </summary>
    public static string Render(string markdown, string? filePath = null, int lineOffset = 0, string? vaultRoot = null)
    {
        // Replace unchecked task checkboxes with clickable buttons BEFORE Markdig processes the text.
        // Line indices must match those used by TaskParser (which calls File.ReadAllLines).
        string preprocessed = markdown;
        if (filePath != null)
        {
            var encodedPath = System.Net.WebUtility.HtmlEncode(filePath);
            var lines = markdown.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var bare = lines[i].TrimEnd('\r');
                var trimmed = bare.TrimStart();
                if (trimmed.StartsWith("- [ ]") || trimmed.StartsWith("* [ ]"))
                    lines[i] = lines[i].Replace("[ ]",
                        $"<button class=\"inline-task-btn\" data-action=\"complete-inline\" data-taskid=\"{encodedPath}:{i + lineOffset}\">☐</button>");
            }
            preprocessed = string.Join('\n', lines);
        }

        // Replace [[link]] with a clickable span BEFORE Markdig processes the text
        var withLinks = WikiLinkRegex.Replace(preprocessed, m =>
        {
            var link = m.Groups[1].Value;
            var encoded = System.Net.WebUtility.HtmlEncode(link);
            return $"<span class=\"wiki-link\" data-wikilink=\"{encoded}\">{encoded}</span>";
        });

        var html = Markdig.Markdown.ToHtml(withLinks, Pipeline);

        // Convert audio <a href> tags to spans so WebView2 doesn't navigate to them
        var afterAudio = AudioLinkRegex.Replace(html, m =>
            $"<span class=\"audio-link\" data-audiolink=\"{m.Groups[1].Value}\">{m.Groups[2].Value}</span>");

        // Resolve relative <img src> to base64 data URIs so WebView2 can load vault images
        // (file:/// URIs are blocked by WebView2's virtual host cross-origin policy)
        if (filePath == null) return afterAudio;
        var noteDir = Path.GetDirectoryName(filePath) ?? string.Empty;

        // Pre-compute the vault root with a trailing separator so StartsWith does a clean
        // directory-prefix check (avoids "/vaultA" matching "/vault" by accident).
        string? vaultRootNormalized = null;
        if (!string.IsNullOrEmpty(vaultRoot))
        {
            vaultRootNormalized = Path.GetFullPath(
                vaultRoot!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar);
        }

        return RelativeImgRegex.Replace(afterAudio, m =>
        {
            var attrs = m.Groups[1].Value;
            var src = m.Groups[2].Value;

            string abs;
            try { abs = Path.GetFullPath(Path.Combine(noteDir, src)); }
            catch { return m.Value; } // malformed path — leave the original tag

            // Defense-in-depth: when a vault root is known, the resolved file MUST live
            // inside it. This blocks ![](../../../Windows/win.ini) and similar.
            if (vaultRootNormalized is not null
                && !abs.StartsWith(vaultRootNormalized, StringComparison.OrdinalIgnoreCase))
                return m.Value;

            if (!File.Exists(abs)) return m.Value; // leave as-is if file missing

            // Whitelist allowed image extensions. Unknown extensions used to fall through to
            // image/png, which would happily base64-encode arbitrary binaries (e.g. .exe).
            var ext = Path.GetExtension(abs).TrimStart('.').ToLowerInvariant();
            string? mime = ext switch
            {
                "png" => "image/png",
                "jpg" or "jpeg" => "image/jpeg",
                "gif" => "image/gif",
                "webp" => "image/webp",
                _ => null
            };
            if (mime is null) return m.Value;

            try
            {
                // H4: re-render is hot — Blazor re-runs Render() on every state change in
                // this component or any sibling, and a 10-image note used to re-read +
                // re-encode every byte every time. Cache by absolute path with the file's
                // mtime as the freshness key so an external edit / re-paste invalidates.
                var mtime = File.GetLastWriteTimeUtc(abs);
                var dataUri = _dataUriCache.AddOrUpdate(
                    abs,
                    addValueFactory: _ => (mtime, BuildDataUri(abs, mime)),
                    updateValueFactory: (_, cached) =>
                        cached.Mtime == mtime ? cached : (mtime, BuildDataUri(abs, mime)));
                return $"<img{attrs} src=\"{dataUri.DataUri}\"";
            }
            catch { return m.Value; }
        });
    }

    private static string BuildDataUri(string absPath, string mime)
    {
        var bytes = File.ReadAllBytes(absPath);
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }
}
