using GithubDownloader.Core.Enums;

namespace GithubDownloader.Core.Installation;

/// <summary>Authorization settings captured for one operation, never shared between requests.</summary>
public sealed record InstallationContext(Platform Platform, string? RootPassword = null);
