using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Github_Downloader_lib;

public static class AssetSelection
{
    public static string GetDefaultRule()
    {
        OSPlatform platform = OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsLinux() ? OSPlatform.Linux : OSPlatform.OSX;
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return string.Empty;

        string linuxRelease = string.Empty;
        if (OperatingSystem.IsLinux())
        {
            try { linuxRelease = File.ReadAllText("/etc/os-release"); }
            catch (IOException) { }
            catch (System.UnauthorizedAccessException) { }
        }
        return GetDefaultRule(platform, RuntimeInformation.OSArchitecture, linuxRelease);
    }

    internal static string GetDefaultRule(OSPlatform platform, Architecture architecture, string linuxRelease = "")
    {
        string aliases = architecture switch
        {
            Architecture.X64 => "{x64,amd64,x86_64}",
            Architecture.Arm64 => "{arm64,aarch64}",
            Architecture.X86 => "{x86,i386,i686,ia32}",
            Architecture.Arm => "{arm32,armhf,armv7,armv7l}",
            Architecture.S390x => "s390x",
            Architecture.Ppc64le => "ppc64le",
            Architecture.LoongArch64 => "{loongarch64,loong64}",
            _ => string.Empty
        };
        if (aliases.Length == 0) return string.Empty;

        string extension;
        if (platform == OSPlatform.Windows) extension = "msi";
        else if (platform == OSPlatform.OSX) extension = "dmg";
        else if (platform == OSPlatform.Linux)
        {
            string[] distributions = linuxRelease.Split('\n')
                .Where(line => line.StartsWith("ID=") || line.StartsWith("ID_LIKE="))
                .SelectMany(line => line[(line.IndexOf('=') + 1)..].Trim().Trim('"', '\'').Split(' '))
                .ToArray();
            extension = distributions.Any(id => id is "debian" or "ubuntu") ? "deb"
                : distributions.Any(id => id is "rhel" or "fedora" or "centos" or "suse" or "opensuse" or "opensuse-leap" or "opensuse-tumbleweed") ? "rpm"
                : "AppImage";
        }
        else return string.Empty;

        // Require the x86 alias immediately before the extension to avoid matching x86_64.
        return $"*{aliases}{(architecture == Architecture.X86 ? "" : "*")}.{extension}";
    }

    // Whitespace separates globs; every inclusion must match, and exclusions always win.
    public static bool Matches(string name, string rule) => CreateRuleMatcher(rule)(name);

    public static List<string> FindMatches(IEnumerable<string> names, string rule)
        => names.Where(CreateRuleMatcher(rule)).ToList();

    private static Func<string, bool> CreateRuleMatcher(string rule)
    {
        List<Func<string, bool>> inclusions = [];
        List<Func<string, bool>> exclusions = [];
        bool excludeNext = false;
        foreach (string token in rule.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "!")
            {
                excludeNext = true;
                continue;
            }
            bool exclude = excludeNext || token.StartsWith('!');
            string pattern = token.StartsWith('!') ? token[1..] : token;
            excludeNext = false;
            Func<string, bool> matcher = pattern.Contains('{')
                ? CreateGlobRegex(pattern).IsMatch
                : name => MatchesGlob(name, pattern);
            (exclude ? exclusions : inclusions).Add(matcher);
        }
        return name => inclusions.Count > 0 && inclusions.All(matches => matches(name))
            && !exclusions.Any(matches => matches(name));
    }

    // Case-sensitive filename globs, with literal alternatives such as {x64,amd64}.
    private static bool MatchesGlob(string name, string pattern)
    {
        int nameIndex = 0;
        int patternIndex = 0;
        int star = -1;
        int retry = 0;
        while (nameIndex < name.Length)
        {
            if (patternIndex < pattern.Length && (pattern[patternIndex] == '?' || pattern[patternIndex] == name[nameIndex]))
            {
                nameIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                star = patternIndex++;
                retry = nameIndex;
            }
            else if (star >= 0)
            {
                patternIndex = star + 1;
                nameIndex = ++retry;
            }
            else return false;
        }
        while (patternIndex < pattern.Length && pattern[patternIndex] == '*') patternIndex++;
        return patternIndex == pattern.Length;
    }

    private static Regex CreateGlobRegex(string pattern)
    {
        StringBuilder expression = new("\\A");
        for (int index = 0; index < pattern.Length; index++)
        {
            char character = pattern[index];
            if (character == '{')
            {
                int end = pattern.IndexOf('}', index + 1);
                if (end >= 0)
                {
                    string[] alternatives = pattern[(index + 1)..end].Split(',');
                    if (alternatives.Length > 1 && alternatives.All(value => value.Length > 0 && !value.Contains('{')))
                    {
                        expression.Append("(?:");
                        expression.AppendJoin('|', alternatives.Select(Regex.Escape));
                        expression.Append(')');
                        index = end;
                        continue;
                    }
                }
            }
            expression.Append(character switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(character.ToString())
            });
        }
        expression.Append("\\z");
        return new Regex(expression.ToString(), RegexOptions.NonBacktracking | RegexOptions.Singleline | RegexOptions.CultureInvariant);
    }
}
