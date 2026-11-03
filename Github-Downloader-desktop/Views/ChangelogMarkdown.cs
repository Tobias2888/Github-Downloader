using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Github_Downloader.Views;

internal static class ChangelogMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePreciseSourceLocation().Build();

    public static string Prepare(string source)
    {
        // Markdown.Avalonia's HTML parser does not recognize standalone comments. Parse
        // their source ranges with CommonMark so comments in code examples stay literal.
        MarkdownDocument document = Markdig.Markdown.Parse(source, Pipeline);
        IEnumerable<SourceSpan> comments = document.Descendants()
            .Where(node => node is HtmlBlock { Type: HtmlBlockType.Comment }
                || node is HtmlInline inline && inline.Tag.StartsWith("<!--", StringComparison.Ordinal))
            .Select(node => node.Span).OrderBy(span => span.Start);

        StringBuilder result = new(source.Length);
        int offset = 0;
        foreach (SourceSpan span in comments)
        {
            int search = Math.Max(span.Start, offset);
            while (search <= span.End)
            {
                int start = source.IndexOf("<!--", search, span.End - search + 1, StringComparison.Ordinal);
                if (start < 0) break;
                int closing = source.IndexOf("-->", start + 4, StringComparison.Ordinal);
                int end = closing < 0 ? source.Length : closing + 3;
                result.Append(source, offset, start - offset);
                for (int index = start; index < end; index++)
                {
                    if (source[index] is '\r' or '\n') result.Append(source[index]);
                }
                offset = search = end;
            }
        }
        result.Append(source, offset, source.Length - offset);
        return result.ToString();
    }
}
