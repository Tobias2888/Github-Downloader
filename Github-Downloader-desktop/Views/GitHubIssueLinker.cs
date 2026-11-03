using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ColorTextBlock.Avalonia;

namespace Github_Downloader.Views;

internal static class GitHubIssueLinker
{
    private static readonly Regex IssuePattern = new(@"(?<![\w/\\.#&])#(\d+)\b");

    public static IEnumerable<CInline> Linkify(IEnumerable<CInline> inlines, string baseUrl, Action<string> openUrl)
    {
        foreach (CInline inline in inlines)
        {
            if (inline is CHyperlink or CCode)
            {
                yield return inline;
                continue;
            }

            if (inline is CSpan span)
            {
                span.Content = Linkify(span.Content, baseUrl, openUrl).ToArray();
                yield return span;
                continue;
            }

            if (inline is not CRun run || string.IsNullOrWhiteSpace(baseUrl))
            {
                yield return inline;
                continue;
            }

            MatchCollection matches = IssuePattern.Matches(run.Text);
            if (matches.Count == 0)
            {
                yield return inline;
                continue;
            }

            int offset = 0;
            foreach (Match match in matches)
            {
                if (match.Index > offset)
                    yield return new CRun { Text = run.Text[offset..match.Index] };

                yield return new CHyperlink(new[] { new CRun { Text = match.Value } })
                {
                    Command = openUrl,
                    CommandParameter = $"{baseUrl}/issues/{match.Groups[1].Value}"
                };
                offset = match.Index + match.Length;
            }

            if (offset < run.Text.Length)
                yield return new CRun { Text = run.Text[offset..] };
        }
    }
}
