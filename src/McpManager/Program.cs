using Avalonia;
using System;
using System.Threading.Tasks;

namespace McpManager;

internal sealed class Program
{
  // Initialization code. Don't use any Avalonia, third-party APIs or any
  // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
  // yet and stuff might break.
  [STAThread]
  public static void Main(string[] args)
  {
    // Prevent unhandled exceptions from crashing the app
    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    {
      Console.Error.WriteLine($"Unhandled exception: {e.ExceptionObject}");
    };

    TaskScheduler.UnobservedTaskException += (_, e) =>
    {
      Console.Error.WriteLine($"Unobserved task exception: {e.Exception}");
      e.SetObserved();
    };

    BuildAvaloniaApp()
      .StartWithClassicDesktopLifetime(args);
  }

  // Avalonia configuration, don't remove; also used by visual designer.
  public static AppBuilder BuildAvaloniaApp()
  {
    return AppBuilder.Configure<App>()
      .UsePlatformDetect()
      .WithInterFont()
      .LogToTrace();
  }
}