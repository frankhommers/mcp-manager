using Avalonia;
using System;
using System.IO;
using System.Threading.Tasks;

namespace McpManager;

internal sealed class Program
{
  private static readonly string CrashLogPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".config", "McpManager", "crash.log");

  private static void LogCrash(string source, object exception)
  {
    try
    {
      string? dir = Path.GetDirectoryName(CrashLogPath);
      if (!string.IsNullOrEmpty(dir))
      {
        Directory.CreateDirectory(dir);
      }

      string entry = $"[{DateTime.UtcNow:O}] {source}: {exception}\n";
      File.AppendAllText(CrashLogPath, entry);
    }
    catch
    {
      // Last resort - can't even log
    }
  }

  // Initialization code. Don't use any Avalonia, third-party APIs or any
  // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
  // yet and stuff might break.
  [STAThread]
  public static void Main(string[] args)
  {
    // Prevent unhandled exceptions from crashing the app
    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    {
      LogCrash("UnhandledException", e.ExceptionObject);
      Console.Error.WriteLine($"Unhandled exception: {e.ExceptionObject}");
    };

    TaskScheduler.UnobservedTaskException += (_, e) =>
    {
      LogCrash("UnobservedTaskException", e.Exception);
      Console.Error.WriteLine($"Unobserved task exception: {e.Exception}");
      e.SetObserved();
    };

    try
    {
      BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);
    }
    catch (Exception ex)
    {
      LogCrash("Main", ex);
      throw;
    }
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