using Avalonia;
using System;
using Github_Downloader.Services;
using LoggerLib;

namespace Github_Downloader;

class Program
{
    internal static DesktopActivationService? DesktopActivation { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static int Main(string[] args)
    {
        bool showWindow = ShouldShowWindow(args);
        using DesktopActivationService activation = DesktopActivationService.ForCurrentUser(showWindow);
        if (!activation.IsPrimaryInstance)
        {
            if (showWindow && !activation.ActivateAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult())
            {
                Console.Error.WriteLine("The running GitHub Downloader desktop app could not be opened. Try again after it finishes starting.");
                return 1;
            }

            return 0;
        }

        DesktopActivation = activation;
        Logger.LogFirstChance = false;
        Logger.LogDebug = false;
        Logger.LogLevel = LogLevel.Info;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    internal static bool ShouldShowWindow(string[] args)
        => !Array.Exists(args, argument => argument == "--hidden");

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions()
            {
                UseDBusMenu = false,
            })
            .WithInterFont()
            .LogToTrace();
}
