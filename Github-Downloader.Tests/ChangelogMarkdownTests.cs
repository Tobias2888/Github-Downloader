using Github_Downloader.Views;
using Xunit;

namespace Github_Downloader.Tests;

public class ChangelogMarkdownTests
{
    [Theory]
    [InlineData("<!-- quick-downloads:start -->\n## Quick downloads\n<!-- quick-downloads:end -->", "\n## Quick downloads\n")]
    [InlineData("Before <!-- internal marker -->after", "Before after")]
    [InlineData("<!-- multiline\n`hidden code`\n# hidden heading\n-->\nVisible", "\n\n\n\nVisible")]
    [InlineData("<!-- unfinished\nInvisible", "\n")]
    [InlineData("`<!-- literal -->`", "`<!-- literal -->`")]
    [InlineData("```html\n<!-- literal -->\n```", "```html\n<!-- literal -->\n```")]
    [InlineData("~~~html\n<!-- literal -->\n~~~", "~~~html\n<!-- literal -->\n~~~")]
    [InlineData("    <!-- literal indented code -->", "    <!-- literal indented code -->")]
    [InlineData("Before <!-- `hidden` -->after", "Before after")]
    [InlineData("<!-- first --><!-- second -->\n# Title", "\n# Title")]
    [InlineData("**Bold**, [link](https://github.com), #123\n\n- [Android] Item", "**Bold**, [link](https://github.com), #123\n\n- [Android] Item")]
    [InlineData("<!-- marker -->tail\n# Title", "tail\n# Title")]
    [InlineData("Before &lt;!-- escaped --&gt; after", "Before &lt;!-- escaped --&gt; after")]
    public void HidesHtmlCommentsWhilePreservingMarkdownAndCode(string source, string expected)
    {
        Assert.Equal(expected, ChangelogMarkdown.Prepare(source));
    }
}
