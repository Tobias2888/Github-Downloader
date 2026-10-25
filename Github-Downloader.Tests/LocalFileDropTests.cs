using System.IO.Compression;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Github_Downloader.Views;
using Github_Downloader_lib;
using Xunit;

namespace Github_Downloader.Tests;

[Collection("Repository dragging")]
public sealed class LocalFileDropTests
{
    private readonly RepositoryDragApplication _application;
    public LocalFileDropTests(RepositoryDragApplication application) => _application = application;

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
    public async Task BrowsedAndDroppedArchivesShareInstallationAndFolderCancellation(bool cancel, string entryPoint)
    {
        string root = Path.Combine(Path.GetTempPath(), "ghd-drop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LocalAppStore previous = LocalApps.Store;
        LocalApps.Store = new(Path.Combine(root, "local-apps.json"));
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
                proxy.Handler = (method, args) =>
                {
                    if (entryPoint != "drop" && method.Name == nameof(IStorageProvider.OpenFilePickerAsync))
                    {
                        FilePickerOpenOptions picker = Assert.IsType<FilePickerOpenOptions>(args![0]);
                        Assert.True(picker.AllowMultiple);
                        Assert.Contains("*.zip", Assert.Single(picker.FileTypeFilter!).Patterns!);
                        return Task.FromResult<IReadOnlyList<IStorageFile>>(sources.Select(path => StorageItem<IStorageFile>(new Uri(path))).ToArray());
                    }
                    Assert.Equal(nameof(IStorageProvider.OpenFolderPickerAsync), method.Name);
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
                    if (!cancel)
                    {
                        while (LocalApps.Store.Read().Count < 2 || ((App)Application.Current!).DownloadStatusViewModel.IsUpdating)
                            await Task.Delay(10, timeout.Token);
                        Assert.Equal(1, home.FindControl<TabStrip>("AppTabs")!.SelectedIndex);
                        Assert.Equal(2, home.FindControl<LocalAppsView>("LocalAppsPanel")!.FindControl<ItemsControl>("AppsList")!.ItemCount);
                    }
                    Assert.Equal(cancel ? 0 : 2, LocalApps.Store.Read().Count);
                    foreach (string source in sources)
                    {
                        Assert.True(File.Exists(source));
                        string extracted = Path.Combine(root, Path.GetFileNameWithoutExtension(source), "application.txt");
                        Assert.Equal(!cancel, File.Exists(extracted));
                        if (!cancel) Assert.Equal(Path.GetFileName(source), File.ReadAllText(extracted));
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
