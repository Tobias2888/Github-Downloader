using System.Text;

namespace GithubDownloader.Core;

internal static class DesktopEntryCommand
{
    // Parse user arguments without invoking a shell. Quote every argument for the desktop Exec grammar.
    internal static string Create(string executable, string arguments)
    {
        List<string> tokens = [];
        StringBuilder token = new();
        char quote = '\0';
        bool escaped = false;
        bool started = false;
        foreach (char character in arguments)
        {
            if (char.IsControl(character)) throw new ArgumentException("Arguments cannot contain control characters.");
            if (escaped) { token.Append(character); escaped = false; started = true; }
            else if (character == '\\' && quote != '\'') { escaped = true; started = true; }
            else if (quote != '\0')
            {
                if (character == quote) quote = '\0';
                else token.Append(character);
            }
            else if (character is '\'' or '"') { quote = character; started = true; }
            else if (character == ' ')
            {
                if (started) { tokens.Add(token.ToString()); token.Clear(); started = false; }
            }
            else { token.Append(character); started = true; }
        }
        if (escaped || quote != '\0') throw new ArgumentException("Close quoted arguments and escape trailing backslashes.");
        if (started) tokens.Add(token.ToString());
        return ArchiveAppLauncher.QuoteExecutable(executable)
            + string.Concat(tokens.Select(value => " " + QuoteArgument(value)));
    }

    private static string QuoteArgument(string value) => "\"" + ArchiveAppLauncher.EscapeValue(value
        .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$"))
        .Replace("%", "%%") + "\"";
}
