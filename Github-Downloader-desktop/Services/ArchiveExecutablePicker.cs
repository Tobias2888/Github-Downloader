using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Github_Downloader.Services;

internal static class ArchiveExecutablePicker
{
    internal static async Task<string?> SelectAsync(Visual owner, string directory, IReadOnlyList<string> candidates)
    {
        TopLevel? topLevel = TopLevel.GetTopLevel(owner);
        if (topLevel is null) return null;
        IStorageFolder? folder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(directory);
        string title = candidates.Count > 1 ? "Several executables found — select the application to launch"
            : "Select the executable in the extracted application folder";
        while (true)
        {
            IReadOnlyList<IStorageFile> selected = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title, AllowMultiple = false, SuggestedStartLocation = folder,
                // KDE treats '*.*' literally, hiding extensionless Linux executables.
                FileTypeFilter = [new FilePickerFileType("All files") { Patterns = ["*"] }]
            });
            if (selected.Count == 0) return null;
            string? path = selected[0].TryGetLocalPath();
            if (path is not null && File.Exists(path)
                && Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal)) return path;
            title = "Choose a file inside the extracted application folder";
        }
    }
}
