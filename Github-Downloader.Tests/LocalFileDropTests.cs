using System.IO.Compression;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Github_Downloader.Views;
using Github_Downloader.Services;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Avalonia.VisualTree;
using Xunit;

namespace Github_Downloader.Tests;

[Collection("Repository dragging")]
public sealed class LocalFileDropTests
{
    private readonly RepositoryDragApplication _application;
    public LocalFileDropTests(RepositoryDragApplication application) => _application = application;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArchiveExecutablePickerStartsInExtractionFolderAndRejectsFilesOutsideIt(bool cancel)
    {
        string root = Path.Combine(Path.GetTempPath(), "ghd-executable-picker-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "extracted");
        Directory.CreateDirectory(folder);
        string inside = Path.Combine(folder, "application");
        string outside = Path.Combine(root, "outside");
        await File.WriteAllTextAsync(inside, "application");
        await File.WriteAllTextAsync(outside, "outside");
        try
        {
            await _application.Dispatch(async () =>
            {
                Window window = new();
                IStorageProvider provider = DispatchProxy.Create<IStorageProvider, StorageProxy>();
                StorageProxy proxy = (StorageProxy)(object)provider;
                int picks = 0;
                proxy.Handler = (method, args) =>
                {
                    if (method.Name == nameof(IStorageProvider.TryGetFolderFromPathAsync))
                    {
                        Assert.Equal(folder, Path.TrimEndingDirectorySeparator(((Uri)args![0]!).LocalPath));
                        return Task.FromResult<IStorageFolder?>(StorageItem<IStorageFolder>(new Uri(folder)));
                    }
                    Assert.Equal(nameof(IStorageProvider.OpenFilePickerAsync), method.Name);
                    FilePickerOpenOptions options = Assert.IsType<FilePickerOpenOptions>(args![0]);
                    Assert.False(options.AllowMultiple);
                    Assert.Equal(folder, options.SuggestedStartLocation!.Path.LocalPath);
                    Assert.Equal(new[] { "*" }, Assert.Single(options.FileTypeFilter!).Patterns);
                    picks++;
                    return Task.FromResult<IReadOnlyList<IStorageFile>>(cancel ? []
                        : [StorageItem<IStorageFile>(new Uri(picks == 1 ? outside : inside))]);
                };
                typeof(TopLevel).GetField("_storageProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, provider);
                window.Show();
                try
                {
                    Assert.Equal(cancel ? null : inside, await ArchiveExecutablePicker.SelectAsync(window, folder, []));
                    Assert.Equal(cancel ? 1 : 2, picks);
                }
                finally { window.Close(); }
            }, CancellationToken.None);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task FileDragsOverTextInputsShowFeedbackAndRejectUnsupportedFilesAndBusyOperations()
    {
        await _application.Dispatch(async () =>
        {
            HomeView home = new();
            Window window = new() { Content = home, Width = 1120, Height = 750 };
            window.Show();
            try
            {
                TextBox target = home.FindControl<TextBox>("TbxUrl")!;
                Border overlay = home.FindControl<Border>("LocalFileDropOverlay")!;
                using DataTransfer files = Files("/tmp/example.ZIP", "/tmp/other.msi");
                DragEventArgs enter = RaiseDrag(target, DragDrop.DragEnterEvent, files);
                Assert.Equal(DragDropEffects.Copy, enter.DragEffects);
                Assert.True(enter.Handled);
                Assert.True(overlay.IsVisible);
                RaiseDrag(target, DragDrop.DragLeaveEvent, files);
                Assert.False(overlay.IsVisible);

                using DataTransfer mixed = Files("/tmp/example.zip", "/tmp/notes.txt");
                Assert.Equal(DragDropEffects.None, RaiseDrag(target, DragDrop.DragOverEvent, mixed).DragEffects);
                Assert.False(overlay.IsVisible);
                using DataTransfer text = new();
                text.Add(DataTransferItem.CreateText("https://github.com/owner/repo"));
                Assert.False(RaiseDrag(target, DragDrop.DragOverEvent, text).Handled);
                using DataTransfer remote = Files("https://example.com/app.zip");
                Assert.False(HomeView.TryGetLocalInstallPaths(remote, out _));

                App app = (App)Application.Current!;
                TaskCompletionSource<bool> operation = new();
                Task<bool> running = app.DownloadStatusViewModel.RunAsync(() => operation.Task);
                try
                {
                    Assert.Equal(DragDropEffects.None, RaiseDrag(target, DragDrop.DragOverEvent, files).DragEffects);
                    Assert.Equal(DragDropEffects.None, RaiseDrag(target, DragDrop.DropEvent, files).DragEffects);
                    Assert.False(overlay.IsVisible);
                }
                finally
                {
                    operation.SetResult(true);
                    await running;
                }
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(false, "drop")]
    [InlineData(true, "drop")]
    [InlineData(false, "sidebar")]
    [InlineData(true, "sidebar")]
    [InlineData(false, "local-apps")]
    [InlineData(true, "local-apps")]
    public async Task LinuxArchivesUseManagedFoldersAndOtherPlatformsCanCancelFolderSelection(bool cancel, string entryPoint)
    {
        string root = Path.Combine(Path.GetTempPath(), "ghd-drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LocalAppStore previous = LocalApps.Store;
        LocalApps.Store = new(Path.Combine(root, "local-apps.json"));
        bool folderCancelled = cancel && !OperatingSystem.IsLinux();
        try
        {
            string[] sources = [Path.Combine(root, "first app.zip"), Path.Combine(root, "second.zip")];
            foreach (string source in sources)
            {
                using ZipArchive archive = ZipFile.Open(source, ZipArchiveMode.Create);
                using StreamWriter writer = new(archive.CreateEntry("application.txt").Open());
                writer.Write(Path.GetFileName(source));
            }
            await _application.Dispatch(async () =>
            {
                HomeView home = new();
                Window window = new() { Content = home, Width = 1120, Height = 750 };
                IStorageProvider provider = DispatchProxy.Create<IStorageProvider, StorageProxy>();
                StorageProxy proxy = (StorageProxy)(object)provider;
                int folderPicks = 0;
                proxy.Handler = (method, args) =>
                {
                    if (method.Name == nameof(IStorageProvider.TryGetFolderFromPathAsync))
                        return Task.FromResult<IStorageFolder?>(StorageItem<IStorageFolder>((Uri)args![0]!));
                    if (method.Name == nameof(IStorageProvider.OpenFilePickerAsync))
                    {
                        FilePickerOpenOptions picker = Assert.IsType<FilePickerOpenOptions>(args![0]);
                        // Data-only archives prompt for an executable; cancellation keeps the extraction.
                        if (!picker.AllowMultiple) return Task.FromResult<IReadOnlyList<IStorageFile>>([]);
                        Assert.NotEqual("drop", entryPoint);
                        Assert.True(picker.AllowMultiple);
                        Assert.Contains("*.zip", Assert.Single(picker.FileTypeFilter!).Patterns!);
                        return Task.FromResult<IReadOnlyList<IStorageFile>>(sources.Select(path => StorageItem<IStorageFile>(new Uri(path))).ToArray());
                    }
                    Assert.Equal(nameof(IStorageProvider.OpenFolderPickerAsync), method.Name);
                    Assert.False(OperatingSystem.IsLinux());
                    folderPicks++;
                    FolderPickerOpenOptions options = Assert.IsType<FolderPickerOpenOptions>(args![0]);
                    string source = sources.Single(path => options.Title!.Contains(Path.GetFileName(path)));
                    IStorageFolder folder = StorageItem<IStorageFolder>(new Uri(Path.Combine(root, Path.GetFileNameWithoutExtension(source))));
                    return Task.FromResult<IReadOnlyList<IStorageFolder>>(cancel ? [] : [folder]);
                };
                typeof(TopLevel).GetField("_storageProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, provider);
                window.Show();
                try
                {
                    if (entryPoint == "drop")
                    {
                        using DataTransfer files = Files(sources.Concat([sources[0]]).ToArray());
                        DragEventArgs drop = RaiseDrag(home.FindControl<TextBox>("TbxUrl")!, DragDrop.DropEvent, files);
                        Assert.Equal(DragDropEffects.Copy, drop.DragEffects);
                    }
                    else
                    {
                        Button browse = entryPoint == "sidebar" ? home.FindControl<Button>("BtnInstallLocal")!
                            : home.FindControl<LocalAppsView>("LocalAppsPanel")!.FindControl<Button>("InstallLocal")!;
                        if (entryPoint == "local-apps") home.FindControl<TabStrip>("AppTabs")!.SelectedIndex = 1;
                        browse.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    // The routed event starts an asynchronous installation; wait for its persisted results.
                    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
                    if (!folderCancelled)
                    {
                        while (LocalApps.Store.Read().Count < 2 || ((App)Application.Current!).DownloadStatusViewModel.IsUpdating)
                            await Task.Delay(10, timeout.Token);
                        Assert.Equal(1, home.FindControl<TabStrip>("AppTabs")!.SelectedIndex);
                        Assert.Equal(2, home.FindControl<LocalAppsView>("LocalAppsPanel")!.FindControl<ItemsControl>("AppsList")!.ItemCount);
                    }
                    Assert.Equal(folderCancelled ? 0 : 2, LocalApps.Store.Read().Count);
                    Assert.Equal(OperatingSystem.IsLinux() ? 0 : cancel ? 1 : 2, folderPicks);
                    foreach (string source in sources)
                    {
                        Assert.True(File.Exists(source));
                        string destination = OperatingSystem.IsLinux()
                            ? LocalApps.Store.Read().Single(app => app.FileName == Path.GetFileName(source)).ExtractionDirectory
                            : Path.Combine(root, Path.GetFileNameWithoutExtension(source));
                        if (OperatingSystem.IsLinux()) Assert.StartsWith(LocalApps.Store.AppsPath + Path.DirectorySeparatorChar, destination);
                        string extracted = Path.Combine(destination, "application.txt");
                        Assert.Equal(!folderCancelled, File.Exists(extracted));
                        if (!folderCancelled) Assert.Equal(Path.GetFileName(source), File.ReadAllText(extracted));
                    }
                }
                finally { window.Close(); }
            }, CancellationToken.None);
        }
        finally
        {
            LocalApps.Store = previous;
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddVersionButtonUpdatesTheSelectedArchiveAndCancellationKeepsItUnchanged(bool cancel)
    {
        string root = Path.Combine(Path.GetTempPath(), "ghd-new-version-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LocalAppStore previous = LocalApps.Store;
        LocalApps.Store = new(Path.Combine(root, "local-apps.json"));
        try
        {
            string first = Path.Combine(root, "example-1.zip");
            string second = Path.Combine(root, "example-2.zip");
            foreach (string source in new[] { first, second })
            {
                using ZipArchive archive = ZipFile.Open(source, ZipArchiveMode.Create);
                using StreamWriter writer = new(archive.CreateEntry("application.txt").Open());
                writer.Write(Path.GetFileName(source));
            }
            string destination = Path.Combine(root, "application");
            Assert.True(await LocalFileInstaller.InstallAsync(first, _ => { }, _ => { }, destination, null, Path.Combine(root, "cache"), 2));
            LocalApp original = Assert.Single(LocalApps.Store.Read());
            await _application.Dispatch(async () =>
            {
                LocalAppsView panel = new();
                Window window = new() { Content = panel, Width = 1400, Height = 700 };
                IStorageProvider provider = DispatchProxy.Create<IStorageProvider, StorageProxy>();
                StorageProxy proxy = (StorageProxy)(object)provider;
                int archivePicks = 0;
                proxy.Handler = (method, args) =>
                {
                    if (method.Name == nameof(IStorageProvider.TryGetFolderFromPathAsync))
                        return Task.FromResult<IStorageFolder?>(StorageItem<IStorageFolder>((Uri)args![0]!));
                    Assert.Equal(nameof(IStorageProvider.OpenFilePickerAsync), method.Name);
                    FilePickerOpenOptions options = Assert.IsType<FilePickerOpenOptions>(args![0]);
                    Assert.False(options.AllowMultiple);
                    if (!options.Title!.StartsWith("Add a version")) return Task.FromResult<IReadOnlyList<IStorageFile>>([]);
                    archivePicks++;
                    Assert.Contains("*.zip", Assert.Single(options.FileTypeFilter!).Patterns!);
                    return Task.FromResult<IReadOnlyList<IStorageFile>>(cancel ? [] : [StorageItem<IStorageFile>(new Uri(second))]);
                };
                typeof(TopLevel).GetField("_storageProvider", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, provider);
                window.Show();
                try
                {
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                    window.UpdateLayout();
                    Button add = Assert.Single(panel.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, "Add version…"));
                    Assert.True(add.IsEffectivelyVisible);
                    add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
                    while (((App)Application.Current!).DownloadStatusViewModel.IsUpdating)
                        await Task.Delay(10, timeout.Token);
                    Assert.Equal(1, archivePicks);
                    LocalApp updated = Assert.Single(LocalApps.Store.Read());
                    Assert.Equal(original.Id, updated.Id);
                    Assert.Equal(cancel ? "example-1.zip" : "example-2.zip", updated.FileName);
                    Assert.Equal(cancel ? 1 : 2, updated.InstallerVersions.Count);
                    Assert.Equal(cancel ? "example-1.zip" : "example-2.zip", File.ReadAllText(Path.Combine(destination, "application.txt")));
                    Assert.Single(panel.FindControl<ItemsControl>("AppsList")!.Items);
                }
                finally { window.Close(); }
            }, CancellationToken.None);
        }
        finally
        {
            LocalApps.Store = previous;
            Directory.Delete(root, true);
        }
    }

    private static DragEventArgs RaiseDrag(Control target, RoutedEvent<DragEventArgs> routedEvent, IDataTransfer data)
    {
        DragEventArgs args = new(routedEvent, data, target, default, KeyModifiers.None) { DragEffects = DragDropEffects.Copy };
        target.RaiseEvent(args);
        return args;
    }

    private static DataTransfer Files(params string[] paths)
    {
        DataTransfer data = new();
        foreach (string path in paths)
            data.Add(DataTransferItem.CreateFile(StorageItem<IStorageFile>(new Uri(path))));
        return data;
    }

    private static T StorageItem<T>(Uri path) where T : class, IStorageItem
    {
        T item = DispatchProxy.Create<T, StorageProxy>();
        ((StorageProxy)(object)item).Handler = (method, _) => method.Name switch
        {
            "get_Path" => path,
            "get_Name" => Path.GetFileName(path.LocalPath),
            "Dispose" => null,
            _ => throw new NotSupportedException(method.Name)
        };
        return item;
    }

    public class StorageProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
