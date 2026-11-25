using GithubDownloader.Core.Installation;

namespace GithubDownloader.Core.Enums;

internal enum InstallationKind { Archive, Debian, Arch, AppImage }

internal static class InstallationKinds
{
    internal static InstallationKind? FromAsset(string name, bool extract)
    {
        if (extract) return InstallationKind.Archive;
        if (InstallationManager.IsArchPackageFile(name)) return InstallationKind.Arch;
        string extension = Path.GetExtension(name);
        if (extension.Equals(".deb", StringComparison.OrdinalIgnoreCase)) return InstallationKind.Debian;
        if (extension.Equals(".appimage", StringComparison.OrdinalIgnoreCase)) return InstallationKind.AppImage;
        return null;
    }
}
