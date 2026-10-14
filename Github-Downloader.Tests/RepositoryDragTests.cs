using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Github_Downloader;
using Github_Downloader.Views;
using Github_Downloader_lib;
using Github_Downloader_lib.Models;
using Xunit;

namespace Github_Downloader.Tests;

[CollectionDefinition("Repository dragging", DisableParallelization = true)]
public class RepositoryDragCollection : ICollectionFixture<RepositoryDragApplication> { }

public sealed class RepositoryDragApplication : IDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(typeof(RepositoryDragApplication));

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .WithInterFont().UseHeadless(new AvaloniaHeadlessPlatformOptions());

    public void Dispose() => Session.Dispose();
}

[Collection("Repository dragging")]
public sealed class RepositoryDragTests
{
    private readonly RepositoryDragApplication _application;

    public RepositoryDragTests(RepositoryDragApplication application) => _application = application;

    [Theory]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    public async Task RowFollowsThePointerAndOrderChangesOnlyOnDrop(int from, int to)
    {
        await _application.Session.Dispatch(async () =>
        {
            ObservableCollection<Repo> repos = NewRepos(4);
            Repo dragged = repos[from];
            int moves = 0;
            repos.CollectionChanged += (_, _) => moves++;
            (Window window, HomeView home) = ShowHome(repos);
            try
            {
                RepoEntryControl[] rows = Rows(home);
                Border handle = rows[from].FindControl<Border>("DragHandle")!;
                Point start = handle.TranslatePoint(new Point(14, 21), window)!.Value;
                Point finish = rows[to].TranslatePoint(new Point(0, rows[to].Bounds.Height / 2), window)!.Value;
                // Keep the same grab offset while crossing the target row's midpoint.
                Point destination = new(start.X + 20, finish.Y + (from < to ? 10 : -10));
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(destination);
                window.UpdateLayout();

                Canvas overlay = home.FindControl<Canvas>("RepoDragOverlay")!;
                RepoEntryControl preview = Assert.IsType<RepoEntryControl>(Assert.Single(overlay.Children));
                Assert.Same(dragged, preview.DataContext);
                Assert.Equal("Hand", handle.Cursor!.ToString());
                Assert.Equal("Hand", home.Cursor!.ToString());
                Point previewPosition = preview.TranslatePoint(default, window)!.Value;
                Assert.Equal(destination.X - start.X,
                    previewPosition.X - rows[from].TranslatePoint(default, window)!.Value.X, 2);
                Assert.Equal(from, repos.IndexOf(dragged));
                Assert.Equal(0, moves);

                await Task.Delay(80);
                Assert.Contains(rows, row => row.DataContext != dragged
                    && row.RenderTransform is TranslateTransform translation && Math.Abs(translation.Y) > 1);
                window.MouseUp(destination, MouseButton.Left);
                await Task.Delay(220);
                Assert.Equal(to, repos.IndexOf(dragged));
                Assert.Equal(1, moves);
                Assert.Empty(overlay.Children);
                Assert.Null(home.Cursor);
                Assert.All(Rows(home), row =>
                {
                    Assert.Null(row.RenderTransform);
                    Assert.Equal(1, row.Opacity);
                    Assert.True(row.IsHitTestVisible);
                });
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task EscapeAndHandleClicksKeepTheOriginalOrder()
    {
        await _application.Session.Dispatch(async () =>
        {
            ObservableCollection<Repo> repos = NewRepos(4);
            Repo[] original = repos.ToArray();
            (Window window, HomeView home) = ShowHome(repos);
            try
            {
                RepoEntryControl[] rows = Rows(home);
                Point start = rows[0].FindControl<Border>("DragHandle")!.TranslatePoint(new Point(14, 21), window)!.Value;
                Point destination = new(start.X, start.Y + rows[0].Bounds.Height * 3);
                window.MouseDown(start, MouseButton.Left);
                window.MouseUp(start, MouseButton.Left);
                Assert.Equal(original, repos);
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(destination);
                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                window.MouseUp(destination, MouseButton.Left);
                await Task.Delay(220);
                Assert.Equal(original, repos);
                Assert.Empty(home.FindControl<Canvas>("RepoDragOverlay")!.Children);
                Assert.All(Rows(home), row => Assert.Null(row.RenderTransform));
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task DraggingAtTheEdgeScrollsAndSavedOrderCanBeReloaded()
    {
        await _application.Session.Dispatch(async () =>
        {
            ObservableCollection<Repo> repos = NewRepos(14);
            Repo dragged = repos[0];
            string path = Path.Combine(Path.GetTempPath(), "ghd-order-" + Guid.NewGuid().ToString("N") + ".json");
            int saves = 0;
            repos.CollectionChanged += (_, _) =>
            {
                File.WriteAllText(path, JsonSerializer.Serialize(repos));
                saves++;
            };
            (Window window, HomeView home) = ShowHome(repos);
            try
            {
                RepoEntryControl row = Rows(home)[0];
                Point start = row.FindControl<Border>("DragHandle")!.TranslatePoint(new Point(14, 21), window)!.Value;
                ScrollViewer scroll = home.FindControl<ScrollViewer>("RepoScroll")!;
                Point edge = scroll.TranslatePoint(new Point(40, scroll.Bounds.Height - 8), window)!.Value;
                window.MouseDown(start, MouseButton.Left);
                window.MouseMove(new Point(start.X, edge.Y));
                await Task.Delay(350);
                Assert.True(scroll.Offset.Y > 30);
                Assert.Equal(0, saves);
                window.MouseUp(new Point(start.X, edge.Y), MouseButton.Left);
                await Task.Delay(220);
                Assert.True(repos.IndexOf(dragged) > 3);
                Assert.Equal(1, saves);
                ObservableCollection<Repo> restored = JsonSerializer.Deserialize<ObservableCollection<Repo>>(File.ReadAllText(path))!;
                Assert.Equal(repos.Select(repo => repo.Name), restored.Select(repo => repo.Name));
            }
            finally
            {
                window.Close();
                File.Delete(path);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ReorderingDuringABatchDoesNotInvalidateTheDownloadEnumerator()
    {
        ObservableCollection<Repo> repos = NewRepos(2);
        Repo first = repos[0];
        Repo second = repos[1];
        string root = Path.Combine(Path.GetTempPath(), "ghd-drag-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        LoggerLib.Logger.LogDir = root;
        LoggerLib.Logger.LogToTerminal = false;
        LoggerLib.Logger.LogFirstChance = false;
        await LoggerLib.Logger.CreateFile();
        List<string> downloads = [];
        try
        {
            bool succeeded = await UpdateManager.UpdateReposAsync(repos, message =>
            {
                if (!message.StartsWith("Downloading owner/", StringComparison.Ordinal)) return;
                downloads.Add(message);
                if (message == $"Downloading {first.Name}") repos.Move(0, 1);
            }, _ => { });

            // These fixtures have no asset URLs; failure must come from missing assets,
            // rather than a collection mutation stopping the remaining repositories.
            Assert.False(succeeded);
            Assert.Equal(new[] { $"Downloading {first.Name}", $"Downloading {second.Name}" }, downloads);
            Assert.Same(first, repos[1]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static ObservableCollection<Repo> NewRepos(int count) => new(Enumerable.Range(0, count)
        .Select(index => new Repo
        {
            Name = $"owner/repository-{index}", Url = $"https://github.com/owner/repository-{index}",
            CurrentInstallTag = "v1", Tag = "v2", AssetNames = ["release.zip"]
        }));

    private static (Window Window, HomeView Home) ShowHome(ObservableCollection<Repo> repos)
    {
        UpdateManager.Repos = repos;
        HomeView home = new();
        Window window = new() { Width = 1400, Height = 780, Content = home };
        window.Show();
        window.UpdateLayout();
        return (window, home);
    }

    private static RepoEntryControl[] Rows(HomeView home) => home.FindControl<ItemsControl>("RepoList")!
        .GetVisualDescendants().OfType<RepoEntryControl>().ToArray();
}
