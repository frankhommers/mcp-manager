using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;

using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CliWrap;
using CliWrap.Buffered;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McpManager.Core.Models;
using McpManager.Core.Services;
using ModelContextProtocol.Client;

namespace McpManager.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
  private readonly IRegistryService _registryService;
  private readonly IConfigExportService _configExportService;
  private readonly IConfigImportService _configImportService;
  private readonly IHttpMcpTester _httpMcpTester;
  private readonly IStdioMcpTester _stdioMcpTester;
  private readonly ITransportDetectionService _transportDetectionService;
  private readonly IShellEnvironmentService _shellEnvironmentService;
  private McpRegistry? _registry;

  [ObservableProperty] private ObservableCollection<McpServerViewModel> _servers = [];

  /// <summary>
  /// Servers grouped by group name for tree display
  /// </summary>
  public IEnumerable<ServerGroupViewModel> ServerGroups =>
    Servers
      .GroupBy(s => s.Group ?? "")
      .OrderBy(g => string.IsNullOrEmpty(g.Key) ? "zzz" : g.Key)
      .Select(g => new ServerGroupViewModel
      {
        GroupName = g.Key,
        Servers = g.OrderBy(s => s.DisplayName).ToList(),
      });

  /// <summary>
  /// Flat list of unique group names for autocomplete
  /// </summary>
  public IEnumerable<string> AvailableGroups =>
    Servers.Select(s => s.Group).Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g);

  [ObservableProperty] private ObservableCollection<TargetFolderViewModel> _targetFolders = [];

  public NavigationPage CurrentPage =>
    ShowSettings ? NavigationPage.Settings :
    SelectedTarget != null ? NavigationPage.Targets :
    SelectedServer != null ? NavigationPage.Servers :
    NavigationPage.Overview;

  public bool ShowOverview => CurrentPage == NavigationPage.Overview;

  public bool IsServerListActive => CurrentPage == NavigationPage.Servers;

  public bool IsTargetListActive => CurrentPage == NavigationPage.Targets;

  [ObservableProperty] private McpServerViewModel? _selectedServer;

  [ObservableProperty] private TargetFolderViewModel? _selectedTarget;

  // When server is selected, clear target selection and hide settings
  partial void OnSelectedServerChanged(McpServerViewModel? value)
  {
    if (value != null)
    {
      SelectedTarget = null;
      ShowSettings = false;
    }

    OnPropertyChanged(nameof(CurrentPage));
    OnPropertyChanged(nameof(ShowOverview));
    OnPropertyChanged(nameof(IsServerListActive));
    OnPropertyChanged(nameof(IsTargetListActive));

    McpTestResult = ""; // Clear test result when switching servers
    DetectResult = ""; // Clear detect result when switching servers
    FetchToolsResult = ""; // Clear fetch tools result when switching servers
  }

  // When target is selected, clear server selection and hide settings
  partial void OnSelectedTargetChanged(TargetFolderViewModel? value)
  {
    if (value != null)
    {
      SelectedServer = null;
      ShowSettings = false;
      _ = value.RefreshExistingServersAsync(_registry?.Settings);
    }

    OnPropertyChanged(nameof(CurrentPage));
    OnPropertyChanged(nameof(ShowOverview));
    OnPropertyChanged(nameof(IsServerListActive));
    OnPropertyChanged(nameof(IsTargetListActive));
  }

  [ObservableProperty] private bool _isLoading;

  [ObservableProperty] private string _statusMessage = "";

  [ObservableProperty] private bool _showSettings;

  partial void OnShowSettingsChanged(bool value)
  {
    OnPropertyChanged(nameof(CurrentPage));
    OnPropertyChanged(nameof(ShowOverview));
    OnPropertyChanged(nameof(IsServerListActive));
    OnPropertyChanged(nameof(IsTargetListActive));
  }

  [ObservableProperty] private string _bridgeCommandHttp = "mcp-proxy {args} {headerArgs} {url}";

  [ObservableProperty] private string _bridgeCommandSse = "mcp-proxy {args} {headerArgs} {url}";

  [ObservableProperty]
  private string _bridgeCommandStreamableHttp =
    "mcp-proxy {args} {headerArgs} --transport streamablehttp {url}";

  [ObservableProperty] private string _bridgeHeaderArgumentTemplate = "--headers {key} {value}";

  [ObservableProperty] private string _selectedThemeMode = "Follow system";

  public ObservableCollection<string> ThemeModes { get; } = ["Follow system", "Dark", "Light"];

  public IEnumerable<McpTransportType> TransportTypes => Enum.GetValues<McpTransportType>();

  /// <summary>
  /// Migrates mcp-proxy commands to include the supported placeholders.
  /// </summary>
  private static string MigrateBridgeCommand(string command)
  {
    if (string.IsNullOrWhiteSpace(command))
    {
      return command;
    }

    string migrated = command.Replace("{headers}", "{headerArgs}");
    if (!migrated.Contains("{args}"))
    {
      int urlIndex = migrated.IndexOf("{url}", StringComparison.Ordinal);
      migrated = urlIndex >= 0
        ? migrated.Insert(urlIndex, "{args} ")
        : $"{migrated} {{args}}";
    }

    ParsedCommand parsed = CommandLineParser.Parse(migrated);
    string executableName = Path.GetFileNameWithoutExtension(parsed.Command);
    if (executableName.Equals("mcp-proxy", StringComparison.OrdinalIgnoreCase) &&
        !migrated.Contains("{headerArgs}"))
    {
      int urlIndex = migrated.IndexOf("{url}", StringComparison.Ordinal);
      migrated = urlIndex >= 0
        ? migrated.Insert(urlIndex, "{headerArgs} ")
        : $"{migrated} {{headerArgs}}";
    }

    return migrated;
  }

  public MainWindowViewModel()
  {
    _registryService = new RegistryService();
    _configExportService = new ConfigExportService();
    _configImportService = new ConfigImportService();
    _httpMcpTester = new HttpMcpTester();
    _stdioMcpTester = new StdioMcpTester();
    _transportDetectionService = new TransportDetectionService();
    _shellEnvironmentService = new ShellEnvironmentService();
    InitializeSidebar();
  }

  public MainWindowViewModel(
    IRegistryService registryService,
    IConfigExportService configExportService,
    IConfigImportService configImportService,
    IHttpMcpTester httpMcpTester,
    IStdioMcpTester stdioMcpTester,
    ITransportDetectionService transportDetectionService,
    IShellEnvironmentService shellEnvironmentService)
  {
    _registryService = registryService;
    _configExportService = configExportService;
    _configImportService = configImportService;
    _httpMcpTester = httpMcpTester;
    _stdioMcpTester = stdioMcpTester;
    _transportDetectionService = transportDetectionService;
    _shellEnvironmentService = shellEnvironmentService;
    InitializeSidebar();
  }

  public async Task InitializeAsync()
  {
    IsLoading = true;
    StatusMessage = "Resolving shell environment...";

    await _shellEnvironmentService.ResolveAsync();

    StatusMessage = "Loading registry...";

    try
    {
      _registry = await _registryService.LoadAsync();
      RefreshFromRegistry();

      // Load settings and migrate old bridge commands to the current placeholders
      string migratedHttpCommand = MigrateBridgeCommand(_registry.Settings.BridgeCommandHttp);
      string migratedSseCommand = MigrateBridgeCommand(_registry.Settings.BridgeCommandSse);
      string migratedStreamableCommand = MigrateBridgeCommand(
        _registry.Settings.BridgeCommandStreamableHttp);
      bool bridgeSettingsMigrated = migratedHttpCommand != _registry.Settings.BridgeCommandHttp ||
                                    migratedSseCommand != _registry.Settings.BridgeCommandSse ||
                                    migratedStreamableCommand != _registry.Settings.BridgeCommandStreamableHttp;

      BridgeCommandHttp = migratedHttpCommand;
      BridgeCommandSse = migratedSseCommand;
      BridgeCommandStreamableHttp = migratedStreamableCommand;
      BridgeHeaderArgumentTemplate = _registry.Settings.BridgeHeaderArgumentTemplate;

      if (!string.IsNullOrEmpty(_registry.Settings.ThemeMode))
      {
        SelectedThemeMode = _registry.Settings.ThemeMode;
      }

      // Save if migrated
      if (bridgeSettingsMigrated)
      {
        _registry.Settings.BridgeCommandHttp = BridgeCommandHttp;
        _registry.Settings.BridgeCommandSse = BridgeCommandSse;
        _registry.Settings.BridgeCommandStreamableHttp = BridgeCommandStreamableHttp;
        await _registryService.SaveAsync(_registry);
      }

      StatusMessage = "";
    }
    catch (Exception ex)
    {
      StatusMessage = $"Error loading: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  private void RefreshFromRegistry()
  {
    if (_registry == null)
    {
      return;
    }

    // Ensure singleton targets exist
    EnsureClipboardTarget();
    EnsureQuickExportTarget();
    EnsureCodexTarget();

    // Unsubscribe from old servers
    foreach (McpServerViewModel server in Servers)
    {
      server.PropertyChanged -= OnServerPropertyChanged;
    }

    Servers.Clear();
    foreach (McpServer server in _registry.Servers)
    {
      McpServerViewModel vm = new(server);
      vm.PropertyChanged += OnServerPropertyChanged;
      Servers.Add(vm);
    }

    foreach (TargetFolderViewModel target in TargetFolders)
    {
      target.PropertyChanged -= OnTargetPropertyChanged;
    }

    TargetFolders.Clear();
    foreach (TargetFolder target in _registry.TargetFolders)
    {
      TargetFolderViewModel vm = new(target, _registry.Servers, _configExportService);
      vm.PropertyChanged += OnTargetPropertyChanged;
      TargetFolders.Add(vm);
    }

    OnPropertyChanged(nameof(ServerGroups));
  }

  private void OnTargetPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
  {
    if (sender is TargetFolderViewModel target &&
        e.PropertyName is nameof(TargetFolderViewModel.Path) or nameof(TargetFolderViewModel.EnableClaudeCode)
          or nameof(TargetFolderViewModel.EnableClaudeDesktop) or nameof(TargetFolderViewModel.EnableOpenCode)
          or nameof(TargetFolderViewModel.EnableCodex))
    {
      _ = target.RefreshExistingServersAsync(_registry?.Settings, debounce: true);
    }
  }

  [RelayCommand]
  private async Task RefreshTargetConfigAsync()
  {
    TargetFolderViewModel? target = SelectedTarget;
    if (target?.CanReadConfig == true)
    {
      await target.RefreshExistingServersAsync(_registry?.Settings);
    }
  }

  private void OnServerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
  {
    // Refresh tree when display-relevant properties change
    if (e.PropertyName is nameof(McpServerViewModel.DisplayName) or nameof(McpServerViewModel.Group)
        or nameof(McpServerViewModel.Name) or nameof(McpServerViewModel.TransportType))
    {
      OnPropertyChanged(nameof(ServerGroups));
    }

    if (e.PropertyName is nameof(McpServerViewModel.DisplayName) or nameof(McpServerViewModel.Name)
        or nameof(McpServerViewModel.Group) or nameof(McpServerViewModel.TransportType) &&
        sender is McpServerViewModel serverVm)
    {
      foreach (TargetFolderViewModel target in TargetFolders)
      {
        ServerSelectionViewModel? selection = target.ServerSelections.FirstOrDefault(s => s.ServerId == serverVm.Id);
        if (selection != null)
        {
          selection.ServerName = serverVm.ListName;
          selection.ServerKey = serverVm.Name;
          selection.Group = serverVm.Group;
          selection.TransportType = serverVm.TransportType;
        }
      }
    }
  }

  private void EnsureClipboardTarget()
  {
    if (_registry == null)
    {
      return;
    }

    // Check if clipboard target already exists
    if (!_registry.TargetFolders.Any(t => t.IsClipboard))
    {
      TargetFolder clipboardTarget = new()
      {
        Name = "Clipboard",
        Path = "",
        IsClipboard = true,
        EnabledClients = TargetClientFlags.ClaudeCode,
      };
      _registry.TargetFolders.Insert(0, clipboardTarget); // Add at the beginning
    }
  }

  private void EnsureQuickExportTarget()
  {
    if (_registry == null)
    {
      return;
    }

    if (!_registry.TargetFolders.Any(t => t.IsQuickExport))
    {
      TargetFolder quickExportTarget = new()
      {
        Name = "Quick Export",
        Path = "",
        IsQuickExport = true,
        EnabledClients = TargetClientFlags.ClaudeCode,
      };

      // Insert after clipboard target (index 1), or at beginning if no clipboard
      int insertIndex = _registry.TargetFolders.FindIndex(t => t.IsClipboard);
      _registry.TargetFolders.Insert(insertIndex >= 0 ? insertIndex + 1 : 0, quickExportTarget);
    }
  }

  private void EnsureCodexTarget()
  {
    if (_registry == null)
    {
      return;
    }

    // Check if Codex global target already exists
    if (!_registry.TargetFolders.Any(t => t.IsGlobal && t.EnabledClients.HasFlag(TargetClientFlags.Codex)))
    {
      string codexConfigPath = _registry.Settings.CodexConfigPath
                               ?? GetDefaultCodexConfigPath();
      string codexPath = Path.GetDirectoryName(codexConfigPath) ?? string.Empty;
      TargetFolder codexTarget = new()
      {
        Name = "Codex CLI",
        Path = codexPath,
        IsGlobal = true,
        EnabledClients = TargetClientFlags.Codex,
      };
      _registry.TargetFolders.Add(codexTarget);
    }
  }

  private static string GetDefaultCodexConfigPath()
  {
    string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    return Path.Combine(home, ".codex", "config.toml");
  }

  private static Window? GetMainWindow()
  {
    if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
      return desktop.MainWindow;
    }

    return null;
  }

  #region Selection Commands

  [RelayCommand]
  private void ClearSelection()
  {
    SelectedServer = null;
    SelectedTarget = null;
    ShowSettings = false;
  }

  [RelayCommand]
  private void SelectServer(McpServerViewModel? server)
  {
    SelectedServer = server;
  }

  [RelayCommand]
  private void OpenSettings()
  {
    SelectedServer = null;
    SelectedTarget = null;
    ShowSettings = true;
  }

  [ObservableProperty] private string _bridgeTestResult = "";

  [RelayCommand]
  private async Task TestBridgeAsync()
  {
    IsLoading = true;
    StatusMessage = "Testing bridge command...";
    BridgeTestResult = "";

    try
    {
      ParsedCommand parsedCommand = CommandLineParser.Parse(BridgeCommandHttp);
      string bridgeCmd = string.IsNullOrWhiteSpace(parsedCommand.Command)
        ? "mcp-proxy"
        : parsedCommand.Command;

      // Try to run --version or --help to check if it exists
      string shell = OperatingSystem.IsWindows() ? "cmd" : "/bin/bash";
      string shellArg = OperatingSystem.IsWindows() ? "/c" : "-c";
      string checkCmd = OperatingSystem.IsWindows()
        ? $"where {QuoteShellArgument(bridgeCmd)}"
        : $"command -v {QuoteShellArgument(bridgeCmd)}";

      BufferedCommandResult result = await Cli.Wrap(shell)
        .WithArguments([shellArg, checkCmd])
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync();

      if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
      {
        string path = result.StandardOutput.Trim().Split('\n').First();

        // Try to get version
        BufferedCommandResult versionResult = await Cli.Wrap(bridgeCmd)
          .WithArguments(["--version"])
          .WithValidation(CommandResultValidation.None)
          .ExecuteBufferedAsync();

        string version = versionResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(versionResult.StandardOutput)
          ? versionResult.StandardOutput.Trim()
          : "version unknown";

        BridgeTestResult = $"✅ Found: {path}\n📦 {version}";
        StatusMessage = $"Bridge OK: {bridgeCmd}";
      }
      else
      {
        BridgeTestResult =
          $"❌ '{bridgeCmd}' not found!\n\nInstall with:\n  pip install mcp-proxy\nor:\n  pipx install mcp-proxy";
        StatusMessage = $"Bridge not found: {bridgeCmd}";
      }
    }
    catch (Exception ex)
    {
      BridgeTestResult = $"❌ Error: {ex.Message}";
      StatusMessage = $"Bridge test failed: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  partial void OnBridgeCommandHttpChanged(string value)
  {
    if (_registry != null)
    {
      _registry.Settings.BridgeCommandHttp = value;
    }

    BridgeTestResult = ""; // Clear test result when command changes
  }

  partial void OnBridgeCommandSseChanged(string value)
  {
    if (_registry != null)
    {
      _registry.Settings.BridgeCommandSse = value;
    }
  }

  partial void OnBridgeCommandStreamableHttpChanged(string value)
  {
    if (_registry != null)
    {
      _registry.Settings.BridgeCommandStreamableHttp = value;
    }
  }

  partial void OnBridgeHeaderArgumentTemplateChanged(string value)
  {
    if (_registry != null)
    {
      _registry.Settings.BridgeHeaderArgumentTemplate = value;
    }
  }

  partial void OnSelectedThemeModeChanged(string value)
  {
    if (Application.Current is not null)
    {
      Application.Current.RequestedThemeVariant = value switch
      {
        "Dark" => ThemeVariant.Dark,
        "Light" => ThemeVariant.Light,
        _ => ThemeVariant.Default,
      };
    }

    if (_registry != null)
    {
      _registry.Settings.ThemeMode = value;
    }
  }

  #endregion

  #region Launch Commands

  [RelayCommand]
  private async Task RunStartupCommandAsync(McpServerViewModel? serverVm)
  {
    if (serverVm == null || string.IsNullOrWhiteSpace(serverVm.StartupCommand))
    {
      return;
    }

    IsLoading = true;
    StatusMessage = $"Launching {serverVm.DisplayName}...";

    try
    {
      string shell = OperatingSystem.IsWindows() ? "cmd" : "/bin/bash";
      string shellArg = OperatingSystem.IsWindows() ? "/c" : "-c";

      Command cmd = Cli.Wrap(shell)
        .WithArguments([shellArg, serverVm.StartupCommand]);

      // Set working directory if specified
      if (!string.IsNullOrWhiteSpace(serverVm.StartupWorkingDirectory))
      {
        cmd = cmd.WithWorkingDirectory(serverVm.StartupWorkingDirectory);
      }

      BufferedCommandResult result = await cmd.ExecuteBufferedAsync();

      StatusMessage = result.ExitCode == 0
        ? $"Launched {serverVm.DisplayName}"
        : $"Launch failed: {result.StandardError}";
    }
    catch (Exception ex)
    {
      StatusMessage = $"Launch error: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  #endregion

  #region Server Commands

  [RelayCommand]
  private void AddServer()
  {
    if (_registry == null)
    {
      return;
    }

    McpServer server = new()
    {
      Name = "new-server",
      DisplayName = "New Server",
      TransportType = McpTransportType.Stdio,
    };

    _registry.Servers.Add(server);
    McpServerViewModel vm = new(server);
    vm.PropertyChanged += OnServerPropertyChanged;
    Servers.Add(vm);
    SelectedServer = vm;

    // Auto-enable new server for all targets
    foreach (TargetFolder target in _registry.TargetFolders)
    {
      target.EnabledServers.Add(server.Id);
    }

    // Refresh target folder server lists
    foreach (TargetFolderViewModel target in TargetFolders)
    {
      target.RefreshServers(_registry.Servers);
    }

    OnPropertyChanged(nameof(ServerGroups));
    StatusMessage = "Server added - don't forget to Save!";
  }

  [RelayCommand]
  private async Task PasteServerFromClipboardAsync()
  {
    if (_registry == null)
    {
      return;
    }

    IClipboard? clipboard = GetMainWindow()?.Clipboard;
    if (clipboard == null)
    {
      StatusMessage = "Clipboard is not available";
      return;
    }

    string? text = await clipboard.GetTextAsync();
    if (string.IsNullOrWhiteSpace(text))
    {
      StatusMessage = "Clipboard is empty";
      return;
    }

    ParsedCommand parsed = CommandLineParser.Parse(text);
    if (string.IsNullOrEmpty(parsed.Command) &&
        parsed.SuggestedTransport == McpTransportType.Stdio)
    {
      StatusMessage = "Clipboard does not contain a runnable command";
      return;
    }

    string nameSeed = !string.IsNullOrEmpty(parsed.Command)
      ? Path.GetFileNameWithoutExtension(parsed.Command)
      : "pasted-server";

    McpServer server = new()
    {
      Name = nameSeed,
      DisplayName = nameSeed,
      TransportType = McpTransportType.Stdio,
    };

    _registry.Servers.Add(server);
    McpServerViewModel vm = new(server);
    vm.PropertyChanged += OnServerPropertyChanged;
    Servers.Add(vm);
    SelectedServer = vm;

    foreach (TargetFolder target in _registry.TargetFolders)
    {
      target.EnabledServers.Add(server.Id);
    }
    foreach (TargetFolderViewModel target in TargetFolders)
    {
      target.RefreshServers(_registry.Servers);
    }

    string summary = vm.ApplyParsed(parsed);
    OnPropertyChanged(nameof(ServerGroups));
    StatusMessage = $"{summary} - don't forget to Save!";
  }

  [RelayCommand]
  private async Task PasteIntoSelectedServerAsync()
  {
    if (SelectedServer == null)
    {
      return;
    }

    IClipboard? clipboard = GetMainWindow()?.Clipboard;
    if (clipboard == null)
    {
      StatusMessage = "Clipboard is not available";
      return;
    }

    string? text = await clipboard.GetTextAsync();
    if (string.IsNullOrWhiteSpace(text))
    {
      StatusMessage = "Clipboard is empty";
      return;
    }

    ParsedCommand parsed = CommandLineParser.Parse(text);
    if (string.IsNullOrEmpty(parsed.Command) &&
        parsed.SuggestedTransport == McpTransportType.Stdio)
    {
      StatusMessage = "Clipboard does not contain a runnable command";
      return;
    }

    StatusMessage = SelectedServer.ApplyParsed(parsed);
  }

  [RelayCommand]
  private void DeleteServer()
  {
    if (SelectedServer == null || _registry == null)
    {
      return;
    }

    McpServer? server = _registry.Servers.FirstOrDefault(s => s.Id == SelectedServer.Id);
    if (server != null)
    {
      _registry.Servers.Remove(server);
      Servers.Remove(SelectedServer);
      SelectedServer = null;

      // Refresh target folder server lists
      foreach (TargetFolderViewModel target in TargetFolders)
      {
        target.RefreshServers(_registry.Servers);
      }

      OnPropertyChanged(nameof(ServerGroups));
    }
  }

  [ObservableProperty] private string _fetchToolsResult = "";
  [ObservableProperty] private bool _isFetchingTools;

  private CancellationTokenSource? _fetchToolsCts;
  private static readonly TimeSpan FetchToolsTimeout = TimeSpan.FromSeconds(60);

  [RelayCommand]
  private void CancelFetchTools()
  {
    try
    {
      _fetchToolsCts?.Cancel();
    }
    catch
    {
      // Ignore - CTS may already be disposed
    }
  }

  [RelayCommand]
  private async Task FetchToolsAsync()
  {
    if (SelectedServer == null)
    {
      return;
    }

    IsLoading = true;
    IsFetchingTools = true;
    FetchToolsResult = "";
    StatusMessage = "Fetching tools from server...";

    // Sync UI state to model before fetching
    SelectedServer.UpdateModel();

    CancellationTokenSource? previousCts = Interlocked.Exchange(ref _fetchToolsCts, new CancellationTokenSource(FetchToolsTimeout));
    previousCts?.Dispose();
    CancellationToken token = _fetchToolsCts!.Token;

    StringBuilder logBuffer = new();
    void AppendLog(string line)
    {
      lock (logBuffer)
      {
        logBuffer.AppendLine(line);
        string snapshot = logBuffer.ToString();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => FetchToolsResult = snapshot);
      }
    }

    try
    {
      List<string> toolNames = await FetchToolNamesAsync(AppendLog, token);

      if (toolNames.Count > 0)
      {
        SelectedServer.SetAvailableTools(toolNames);
        AppendLog($"[OK] Found {toolNames.Count} tools: {string.Join(", ", toolNames)}");
        StatusMessage = $"Found {toolNames.Count} tools";
      }
      else
      {
        AppendLog("[WARN] No tools found or server did not respond.");
        StatusMessage = "No tools found";
      }
    }
    catch (OperationCanceledException)
    {
      AppendLog(token.IsCancellationRequested && !(_fetchToolsCts?.IsCancellationRequested ?? false)
        ? "[TIMEOUT] Fetch tools cancelled (timeout)"
        : "[CANCELLED] Fetch tools cancelled by user");
      StatusMessage = "Fetch tools cancelled";
    }
    catch (Exception ex)
    {
      string details = ex.InnerException != null
        ? $"[ERROR] {ex.Message}\n        Inner: {ex.InnerException.Message}"
        : $"[ERROR] {ex.Message}";
      AppendLog(details);
      string path = Environment.GetEnvironmentVariable("PATH") ?? "(not set)";
      AppendLog($"[DEBUG] PATH: {path}");
      StatusMessage = $"Fetch tools error: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
      IsFetchingTools = false;
      CancellationTokenSource? cts = Interlocked.Exchange(ref _fetchToolsCts, null);
      cts?.Dispose();
    }
  }

  private async Task<List<string>> FetchToolNamesAsync(Action<string>? log = null, CancellationToken cancellationToken = default)
  {
    if (SelectedServer == null || _registry == null)
    {
      return [];
    }

    McpServer? server = _registry.Servers.FirstOrDefault(s => s.Id == SelectedServer.Id);
    if (server == null)
    {
      return [];
    }

    return await FetchToolNamesForServerAsync(server, log, cancellationToken);
  }

  private async Task<List<string>> FetchToolNamesForServerAsync(
    McpServer server,
    Action<string>? log = null,
    CancellationToken cancellationToken = default)
  {
    // Callers may pass CancellationToken.None; in that case fall back to the default timeout so
    // slow-starting servers (uvx fresh install, lazy tool discovery, retry loops) have a chance.
    CancellationTokenSource? fallbackCts = null;
    if (!cancellationToken.CanBeCanceled)
    {
      fallbackCts = new CancellationTokenSource(FetchToolsTimeout);
      cancellationToken = fallbackCts.Token;
    }

    IClientTransport transport;
    if (server.TransportType == McpTransportType.Stdio)
    {
      if (string.IsNullOrEmpty(server.Command))
      {
        fallbackCts?.Dispose();
        return [];
      }

      Dictionary<string, string?> envVars = server.EnvironmentVariables
        .ToDictionary(kv => kv.Key, kv => (string?)kv.Value);

      log?.Invoke($"[INFO] Starting stdio: {server.Command} {string.Join(" ", server.Args)}");

      transport = new StdioClientTransport(new StdioClientTransportOptions
      {
        Name = server.DisplayName,
        Command = server.Command,
        Arguments = server.Args,
        EnvironmentVariables = envVars.Count > 0 ? envVars : null,
        StandardErrorLines = line =>
        {
          try
          {
            log?.Invoke($"[stderr] {line}");
          }
          catch
          {
            // Never let UI callback exceptions kill the SDK's stderr pump thread
          }
        },
      });
    }
    else
    {
      if (string.IsNullOrEmpty(server.Url))
      {
        fallbackCts?.Dispose();
        return [];
      }

      HttpTransportMode mode = server.TransportType switch
      {
        McpTransportType.Sse => HttpTransportMode.Sse,
        McpTransportType.StreamableHttp => HttpTransportMode.StreamableHttp,
        _ => HttpTransportMode.AutoDetect,
      };

      log?.Invoke($"[INFO] Connecting {mode}: {server.Url}");

      Dictionary<string, string>? headers = server.HttpHeaders.Count > 0 ? server.HttpHeaders : null;
      transport = CreateHttpTransport(server.Url, mode, headers);
    }

    McpClient? client = null;
    try
    {
      log?.Invoke("[INFO] Initializing MCP session...");
      client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
      log?.Invoke("[INFO] Session ready, requesting tools/list...");
      IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
      return tools.Select(t => t.Name).ToList();
    }
    finally
    {
      // Dispose the client with its own shutdown budget so that a user-cancelled or timed-out
      // operation does not leak background read tasks that later surface as
      // UnobservedTaskException and crash the process in Release builds.
      if (client != null)
      {
        try
        {
          using CancellationTokenSource disposeCts = new(TimeSpan.FromSeconds(5));
          ValueTask disposeTask = client.DisposeAsync();
          if (!disposeTask.IsCompleted)
          {
            await disposeTask.AsTask().WaitAsync(disposeCts.Token).ConfigureAwait(false);
          }
          else
          {
            await disposeTask.ConfigureAwait(false);
          }
        }
        catch
        {
          // Swallow dispose errors from MCP SDK; we are already on the error path.
        }
      }

      fallbackCts?.Dispose();
    }
  }

  private static HttpClientTransport CreateHttpTransport(
    string url,
    HttpTransportMode mode,
    Dictionary<string, string>? headers = null)
  {
    HttpClient httpClient = new();
    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("McpManager/1.0");

    if (headers is { Count: > 0 })
    {
      foreach ((string key, string value) in headers)
      {
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
      }
    }

    HttpClientTransportOptions httpOptions = new()
    {
      Endpoint = new Uri(url),
      TransportMode = mode,
    };

    return new HttpClientTransport(httpOptions, httpClient, null!, true);
  }

  private static Dictionary<string, string> CollectEnvironmentVariables(McpServerViewModel? server)
  {
    Dictionary<string, string> envVars = new();
    if (server == null)
    {
      return envVars;
    }

    foreach (KeyValuePairViewModel kvp in server.EnvironmentVariables)
    {
      if (!string.IsNullOrWhiteSpace(kvp.Key))
      {
        envVars[kvp.Key] = kvp.Value;
      }
    }

    return envVars;
  }

  [RelayCommand]
  private async Task FetchAllTargetToolsAsync()
  {
    TargetFolderViewModel? target = SelectedTarget;
    if (target == null || _registry == null)
    {
      return;
    }

    IsLoading = true;
    StatusMessage = "Fetching tools for target servers...";
    int totalFound = 0;

    try
    {
      // Get enabled servers that don't have tools yet
      List<ServerSelectionViewModel> serversToFetch = target.ServerSelections
        .Where(s => s.IsEnabled && !s.HasToolOverrides)
        .ToList();

      if (serversToFetch.Count == 0)
      {
        StatusMessage = "All enabled servers already have tools";
        return;
      }

      foreach (ServerSelectionViewModel selection in serversToFetch)
      {
        McpServer? server = _registry.Servers.FirstOrDefault(s => s.Id == selection.ServerId);
        if (server == null)
        {
          continue;
        }

        StatusMessage = $"Fetching tools from {server.DisplayName}...";
        selection.IsFetchingTools = true;
        selection.ToolFetchStatus = "Fetching tools...";

        try
        {
          List<string> toolNames = await FetchToolNamesForServerAsync(server);
          if (toolNames.Count > 0)
          {
            // Update the server model
            server.KnownTools = toolNames;

            // Find and update the McpServerViewModel if it exists
            McpServerViewModel? serverVm = Servers.FirstOrDefault(s => s.Id == server.Id);
            serverVm?.SetAvailableTools(toolNames);

            totalFound += toolNames.Count;
          }

          selection.ToolFetchStatus = toolNames.Count > 0
            ? $"Found {toolNames.Count} tools."
            : "No tools returned. Existing choices were kept.";
        }
        catch (Exception ex)
        {
          selection.ToolFetchStatus = $"Could not fetch tools: {ex.Message}";
        }
        finally
        {
          selection.IsFetchingTools = false;
        }
      }

      // Refresh target's tool overrides for all servers
      target.RefreshServers(_registry.Servers);

      StatusMessage = totalFound > 0
        ? $"Discovered {totalFound} tools across {serversToFetch.Count} servers"
        : "No new tools discovered";
    }
    finally
    {
      IsLoading = false;
    }
  }

  [RelayCommand]
  private async Task FetchToolsForServerAsync(ServerSelectionViewModel? selection)
  {
    TargetFolderViewModel? target = SelectedTarget;
    if (selection == null || !selection.IsEnabled || _registry == null || target == null ||
        !target.ServerSelections.Contains(selection))
    {
      return;
    }

    // Sync current UI state to model
    Servers.FirstOrDefault(s => s.Id == selection.ServerId)?.UpdateModel();

    McpServer? server = _registry.Servers.FirstOrDefault(s => s.Id == selection.ServerId);
    if (server == null)
    {
      return;
    }

    IsLoading = true;
    StatusMessage = $"Fetching tools from {server.DisplayName}...";
    selection.IsFetchingTools = true;
    selection.ToolFetchStatus = "Fetching tools...";

    try
    {
      List<string> toolNames = await FetchToolNamesForServerAsync(server);
      if (toolNames.Count > 0)
      {
        server.KnownTools = toolNames;

        McpServerViewModel? serverVm = Servers.FirstOrDefault(s => s.Id == server.Id);
        serverVm?.SetAvailableTools(toolNames);

        target.RefreshServers(_registry.Servers);
        selection.ToolFetchStatus = $"Found {toolNames.Count} tools.";
        StatusMessage = $"Found {toolNames.Count} tools for {server.DisplayName}";
      }
      else
      {
        selection.ToolFetchStatus = "No tools returned. Existing choices were kept.";
        StatusMessage = $"No tools found for {server.DisplayName}";
      }
    }
    catch (Exception ex)
    {
      selection.ToolFetchStatus = $"Could not fetch tools: {ex.Message}";
      StatusMessage = $"Failed to fetch tools from {server.DisplayName}: {ex.Message}";
    }
    finally
    {
      selection.IsFetchingTools = false;
      IsLoading = false;
    }
  }

  [RelayCommand]
  private void AllowAllTargetTools(ServerSelectionViewModel? serverSelection)
  {
    serverSelection?.AllowAllTools();
  }

  [RelayCommand]
  private void DenyAllTargetTools(ServerSelectionViewModel? serverSelection)
  {
    serverSelection?.DenyAllTools();
  }

  [ObservableProperty] private string _detectResult = "";

  [RelayCommand]
  private async Task DetectTransportTypeAsync()
  {
    if (SelectedServer == null || string.IsNullOrWhiteSpace(SelectedServer.Url))
    {
      return;
    }

    SelectedServer.UpdateModel();
    IsLoading = true;
    DetectResult = "";
    StatusMessage = "Detecting transport type (sending MCP initialize)...";

    try
    {
      McpServer? model = _registry?.Servers.FirstOrDefault(s => s.Id == SelectedServer.Id);
      Dictionary<string, string>? headers = model?.HttpHeaders is { Count: > 0 } h ? h : null;
      TransportDetectionResult result = await _transportDetectionService.DetectTransportTypeAsync(
        SelectedServer.Url, headers);

      if (result.Success && result.DetectedType.HasValue)
      {
        SelectedServer.TransportType = result.DetectedType.Value;
        StatusMessage = result.Message ?? $"Detected: {result.DetectedType.Value.GetDisplayName()}";

        string header = $"✅ Detected: {result.DetectedType.Value.GetDisplayName()}";
        if (result.ServerName != null)
        {
          header += $"\n📦 Server: {result.ServerName}";
          if (result.ServerVersion != null)
          {
            header += $" v{result.ServerVersion}";
          }
        }

        DetectResult = header + "\n\n" + (result.RawResponse ?? "");
      }
      else
      {
        StatusMessage = result.Message ?? "Could not detect transport type";
        DetectResult = $"❌ {result.Message}\n\n{result.RawResponse ?? ""}";
      }
    }
    catch (Exception ex)
    {
      StatusMessage = $"Detection error: {ex.Message}";
      DetectResult = $"❌ Error: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  [RelayCommand]
  private async Task CopyDetectResultAsync()
  {
    if (string.IsNullOrEmpty(DetectResult))
    {
      return;
    }

    IClipboard? clipboard = GetMainWindow()?.Clipboard;
    if (clipboard != null)
    {
      await clipboard.SetTextAsync(DetectResult);
      StatusMessage = "Copied to clipboard";
    }
  }

  [ObservableProperty] private string _mcpTestResult = "";

  [RelayCommand]
  private async Task CopyMcpTestResultAsync()
  {
    if (string.IsNullOrEmpty(McpTestResult))
    {
      return;
    }

    IClipboard? clipboard = GetMainWindow()?.Clipboard;
    if (clipboard != null)
    {
      await clipboard.SetTextAsync(McpTestResult);
      StatusMessage = "Copied to clipboard";
    }
  }

  [RelayCommand]
  private async Task TestMcpDirectAsync()
  {
    if (SelectedServer == null)
    {
      return;
    }

    SelectedServer.UpdateModel();
    IsLoading = true;
    McpTestResult = "";

    if (SelectedServer.IsStdio)
    {
      if (string.IsNullOrWhiteSpace(SelectedServer.Command))
      {
        McpTestResult = "❌ No command configured";
        StatusMessage = "Test failed: no command";
        IsLoading = false;
        return;
      }

      StatusMessage = "Testing stdio command directly...";
      McpServer? server = _registry?.Servers.FirstOrDefault(s => s.Id == SelectedServer.Id);
      if (server == null)
      {
        McpTestResult = "❌ Server configuration not found";
        StatusMessage = "Test failed: server configuration not found";
        IsLoading = false;
        return;
      }

      StdioMcpTestResult result = await _stdioMcpTester.TestInitializeAsync(server);
      McpTestResult = result.ResultText;
      StatusMessage = result.StatusMessage;
      IsLoading = false;
    }
    else
    {
      // For HTTP, do direct HTTP POST
      if (string.IsNullOrWhiteSpace(SelectedServer.Url))
      {
        McpTestResult = "❌ No URL configured";
        StatusMessage = "Test failed: no URL";
        IsLoading = false;
        return;
      }

      StatusMessage = "Testing via direct HTTP POST...";
      McpServer? model = _registry?.Servers.FirstOrDefault(s => s.Id == SelectedServer.Id);
      Dictionary<string, string> httpHeaders = model?.HttpHeaders ?? new();
      await TestHttpDirectAsync(SelectedServer.Url, SelectedServer.TransportType, httpHeaders);
    }
  }

  [RelayCommand]
  private async Task TestMcpBridgeAsync()
  {
    if (SelectedServer == null)
    {
      return;
    }

    SelectedServer.UpdateModel();
    IsLoading = true;
    McpTestResult = "";

    if (SelectedServer.IsStdio)
    {
      McpTestResult = "ℹ️ Stdio servers don't use a bridge - use 'Test Direct' instead.";
      StatusMessage = "Stdio doesn't need bridge";
      IsLoading = false;
      return;
    }

    if (string.IsNullOrWhiteSpace(SelectedServer.Url))
    {
      McpTestResult = "❌ No URL configured";
      StatusMessage = "Test failed: no URL";
      IsLoading = false;
      return;
    }

    // Get bridge command for transport type
    string bridgeCommand = SelectedServer.TransportType switch
    {
      McpTransportType.Http => BridgeCommandHttp,
      McpTransportType.Sse => BridgeCommandSse,
      McpTransportType.StreamableHttp => BridgeCommandStreamableHttp,
      _ => BridgeCommandSse,
    };

    McpServer? serverModel = _registry?.Servers.FirstOrDefault(s => s.Id == SelectedServer.Id);
    IReadOnlyDictionary<string, string> headers = serverModel?.HttpHeaders
      ?? new Dictionary<string, string>();
    ResolvedCommand resolvedCommand = BridgeCommandResolver.Resolve(
      bridgeCommand,
      SelectedServer.Url,
      null,
      BridgeHeaderArgumentTemplate,
      headers);

    StatusMessage = "Testing via bridge...";
    await TestViaCommandAsync(resolvedCommand);
  }

  private async Task TestViaCommandAsync(ResolvedCommand resolvedCommand)
  {
    // Send MCP initialize request via stdin and read response
    string initRequest =
      """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"MCP Manager Test","version":"1.0.0"}}}""";

    string fullCommand = FormatShellCommand(resolvedCommand, false);
    string displayCommand = FormatShellCommand(resolvedCommand, true);

    // Build debug header
    StringBuilder debugInfo = new();
    debugInfo.AppendLine("🔧 Bridge");
    debugInfo.AppendLine();
    debugInfo.AppendLine("📤 Command:");
    debugInfo.AppendLine($"  {displayCommand}");
    debugInfo.AppendLine();
    debugInfo.AppendLine("📥 Stdin (MCP initialize):");
    debugInfo.AppendLine($"  {initRequest}");
    debugInfo.AppendLine();

    try
    {
      string shell = OperatingSystem.IsWindows() ? "cmd" : "/bin/bash";
      string shellArg = OperatingSystem.IsWindows() ? "/c" : "-c";
      string shellCommand = $"(echo '{initRequest.Replace("'", "'\\''")}'; sleep 5) | {fullCommand}";

      ProcessStartInfo psi = new()
      {
        FileName = shell,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        RedirectStandardInput = false,
        UseShellExecute = false,
        CreateNoWindow = true,
      };
      psi.ArgumentList.Add(shellArg);
      psi.ArgumentList.Add(shellCommand);

      using Process? process = Process.Start(psi);
      if (process is null)
      {
        debugInfo.AppendLine("❌ Failed to start shell process");
        McpTestResult = debugInfo.ToString();
        StatusMessage = "Test failed: could not start process";
        return;
      }

      using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
      Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
      Task<string> stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

      try
      {
        await Task.WhenAll(stdoutTask, stderrTask);
      }
      catch (OperationCanceledException)
      {
      }

      try
      {
        if (!process.HasExited)
        {
          process.Kill(true);
        }
      }
      catch
      {
      }

      string output = stdoutTask.IsCompletedSuccessfully ? stdoutTask.Result.Trim() : "";
      string error = stderrTask.IsCompletedSuccessfully ? stderrTask.Result.Trim() : "";



      debugInfo.AppendLine("📤 Stdout:");
      debugInfo.AppendLine(string.IsNullOrEmpty(output) ? "  (empty)" : $"  {output}");
      debugInfo.AppendLine();
      debugInfo.AppendLine("📤 Stderr:");
      debugInfo.AppendLine(string.IsNullOrEmpty(error) ? "  (empty)" : $"  {error}");
      debugInfo.AppendLine();

      if (string.IsNullOrEmpty(output))
      {
        debugInfo.AppendLine("❌ No response received");
        McpTestResult = debugInfo.ToString();
        StatusMessage = "MCP test failed";
      }
      else if (output.Contains("\"result\"") && output.Contains("serverInfo"))
      {
        // Try to parse server info
        string serverName = "Unknown";
        string serverVersion = "";
        try
        {
          Match match = System.Text.RegularExpressions.Regex.Match(output, @"""name""\s*:\s*""([^""]+)""");
          if (match.Success)
          {
            serverName = match.Groups[1].Value;
          }

          match = System.Text.RegularExpressions.Regex.Match(output, @"""version""\s*:\s*""([^""]+)""");
          if (match.Success)
          {
            serverVersion = match.Groups[1].Value;
          }
        }
        catch
        {
        }

        debugInfo.AppendLine($"✅ MCP Server Connected!");
        debugInfo.AppendLine(
          $"📦 Server: {serverName}" + (string.IsNullOrEmpty(serverVersion) ? "" : $" v{serverVersion}"));
        McpTestResult = debugInfo.ToString();
        StatusMessage = $"MCP OK: {serverName}";
      }
      else if (output.Contains("\"error\""))
      {
        debugInfo.AppendLine("⚠️ MCP Error Response");
        McpTestResult = debugInfo.ToString();
        StatusMessage = "MCP returned error";
      }
      else
      {
        debugInfo.AppendLine("❓ Unexpected response (no MCP serverInfo found)");
        McpTestResult = debugInfo.ToString();
        StatusMessage = "MCP test: unexpected response";
      }
    }
    catch (OperationCanceledException)
    {
      debugInfo.AppendLine("❌ Connection timed out (10s)");
      debugInfo.AppendLine("The server may not be running or the command may be incorrect.");
      McpTestResult = debugInfo.ToString();
      StatusMessage = "MCP test timed out";
    }
    catch (Exception ex)
    {
      debugInfo.AppendLine($"❌ Error: {ex.Message}");
      McpTestResult = debugInfo.ToString();
      StatusMessage = $"MCP test error: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  private static string FormatShellCommand(ResolvedCommand command, bool redactHeaderValues)
  {
    List<string> parts = [QuoteShellArgument(command.Command)];

    for (int i = 0; i < command.Arguments.Count; i++)
    {
      string argument = redactHeaderValues && command.SensitiveArgumentIndexes.Contains(i)
        ? "***"
        : command.Arguments[i];
      parts.Add(QuoteShellArgument(argument));
    }

    return string.Join(" ", parts);
  }

  private static string QuoteShellArgument(string value)
  {
    if (OperatingSystem.IsWindows())
    {
      return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    return $"'{value.Replace("'", "'\\''")}'";
  }

  /// <summary>
  /// Test HTTP MCP server directly via HTTP POST (for servers that don't need mcp-proxy)
  /// </summary>
  private async Task TestHttpDirectAsync(string url, McpTransportType transportType, Dictionary<string, string>? httpHeaders = null)
  {
    HttpMcpTestResult result = await _httpMcpTester.TestInitializeAsync(url, transportType, httpHeaders);
    McpTestResult = result.ResultText;
    StatusMessage = result.StatusMessage;
    IsLoading = false;
  }

  #endregion

  #region Target Folder Commands

  [RelayCommand]
  private async Task AddTargetFolderAsync()
  {
    if (_registry == null)
    {
      return;
    }

    Window? window = GetMainWindow();
    if (window == null)
    {
      return;
    }

    // Collect existing global typed target flags to disable in dialog
    HashSet<TargetClientFlags> existingGlobal = [];
    foreach (TargetFolder t in _registry.TargetFolders)
    {
      if (t.IsGlobal && !t.IsClipboard && !t.IsQuickExport)
      {
        existingGlobal.Add(t.EnabledClients);
      }
    }

    // Also check if clipboard already exists
    bool hasClipboard = _registry.TargetFolders.Any(t => t.IsClipboard);

    Views.NewTargetDialog dialog = new(existingGlobal);
    TargetFolder? result = await dialog.ShowDialog<TargetFolder?>(window);
    if (result == null)
    {
      return;
    }

    _registry.TargetFolders.Add(result);
    TargetFolderViewModel vm = new(result, _registry.Servers, _configExportService);
    vm.PropertyChanged += OnTargetPropertyChanged;
    TargetFolders.Add(vm);
    SelectedTarget = vm;
  }


  [RelayCommand]
  private void DeleteTargetFolder()
  {
    if (SelectedTarget == null || _registry == null)
    {
      return;
    }

    // Cannot delete clipboard target
    if (SelectedTarget.IsClipboard)
    {
      return;
    }

    // Cannot delete quick export target
    if (SelectedTarget.IsQuickExport)
    {
      return;
    }

    TargetFolder? target = _registry.TargetFolders.FirstOrDefault(t => t.Id == SelectedTarget.Id);
    if (target != null)
    {
      _registry.TargetFolders.Remove(target);
      SelectedTarget.PropertyChanged -= OnTargetPropertyChanged;
      TargetFolders.Remove(SelectedTarget);
      SelectedTarget = null;
    }
  }

  [RelayCommand]
  private async Task BrowseTargetPathAsync()
  {
    TargetFolderViewModel? target = SelectedTarget;
    if (target == null)
    {
      return;
    }

    Window? window = GetMainWindow();
    if (window == null)
    {
      return;
    }

    IReadOnlyList<IStorageFolder> folders = await window.StorageProvider.OpenFolderPickerAsync(
      new FolderPickerOpenOptions
      {
        Title = "Select Target Folder",
        AllowMultiple = false,
      });

    if (folders.Count > 0)
    {
      target.Path = folders[0].Path.LocalPath;
      await target.RefreshExistingServersAsync(_registry?.Settings);
    }
  }

  #endregion

  #region Import Commands

  [RelayCommand]
  private async Task ImportFromFileAsync()
  {
    Window? window = GetMainWindow();
    if (window == null || _registry == null)
    {
      return;
    }

    IReadOnlyList<IStorageFile> files = await window.StorageProvider.OpenFilePickerAsync(
      new FilePickerOpenOptions
      {
        Title = "Import MCP Configuration",
        AllowMultiple = false,
        FileTypeFilter =
        [
          new FilePickerFileType("MCP Config Files")
          {
            Patterns = ["*.json", "*.toml"],
          },
        ],
      });

    if (files.Count == 0)
    {
      return;
    }

    IsLoading = true;
    StatusMessage = "Importing...";

    try
    {
      string filePath = files[0].Path.LocalPath;
      List<McpServer> importedServers = await _configImportService.ImportAutoDetectAsync(filePath);

      int addedCount = 0;
      foreach (McpServer server in importedServers)
      {
        if (_registry.Servers.Any(s => s.Id == server.Id))
        {
          continue;
        }

        // Check if server with same name exists
        if (_registry.Servers.Any(s => s.Name == server.Name))
        {
          server.Name = $"{server.Name}-imported";
          server.DisplayName = $"{server.DisplayName} (Imported)";
        }

        _registry.Servers.Add(server);
        McpServerViewModel importedVm = new(server);
        importedVm.PropertyChanged += OnServerPropertyChanged;
        Servers.Add(importedVm);
        addedCount++;
      }

      // Refresh target folder server lists
      foreach (TargetFolderViewModel target in TargetFolders)
      {
        target.RefreshServers(_registry.Servers);
      }

      StatusMessage = $"Imported {addedCount} servers from {Path.GetFileName(filePath)}";
    }
    catch (Exception ex)
    {
      StatusMessage = $"Import error: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  [RelayCommand]
  private async Task ImportFromClaudeDesktopAsync()
  {
    if (_registry == null)
    {
      return;
    }

    string? configPath = _registry.Settings.ClaudeDesktopConfigPath;
    if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
    {
      StatusMessage = "Claude Desktop config not found";
      return;
    }

    IsLoading = true;
    StatusMessage = "Importing from Claude Desktop...";

    try
    {
      List<McpServer> importedServers = await _configImportService.ImportFromClaudeCodeAsync(configPath);
      int addedCount = AddImportedServers(importedServers);
      StatusMessage = $"Imported {addedCount} servers from Claude Desktop";
    }
    catch (Exception ex)
    {
      StatusMessage = $"Import error: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  [RelayCommand]
  private async Task ImportFromCodexAsync()
  {
    if (_registry == null)
    {
      return;
    }

    string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string configPath = Path.Combine(homeDir, ".codex", "config.toml");

    if (!File.Exists(configPath))
    {
      StatusMessage = "Codex config not found at ~/.codex/config.toml";
      return;
    }

    IsLoading = true;
    StatusMessage = "Importing from Codex...";

    try
    {
      List<McpServer> importedServers = await _configImportService.ImportFromCodexAsync(configPath);
      int addedCount = AddImportedServers(importedServers);
      StatusMessage = $"Imported {addedCount} servers from Codex";
    }
    catch (Exception ex)
    {
      StatusMessage = $"Import error: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  [RelayCommand]
  private async Task ScanForMcpConfigsAsync()
  {
    if (_registry == null)
    {
      return;
    }

    IsLoading = true;
    StatusMessage = "Scanning for .mcp.json files...";

    try
    {
      string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
      List<string> foundFiles = new();

      // Search common locations
      string[] searchPaths = new[]
      {
        homeDir,
        Path.Combine(homeDir, "Projects"),
        Path.Combine(homeDir, "Developer"),
        Path.Combine(homeDir, "Code"),
        Path.Combine(homeDir, "repos"),
        Path.Combine(homeDir, "Repos"),
        Path.Combine(homeDir, "src"),
        Path.Combine(homeDir, "dev"),
        Path.Combine(homeDir, "Documents"),
      };

      foreach (string searchPath in searchPaths)
      {
        if (!Directory.Exists(searchPath))
        {
          continue;
        }

        try
        {
          string[] files = Directory.GetFiles(searchPath, ".mcp.json", SearchOption.AllDirectories);
          foundFiles.AddRange(files);
        }
        catch (UnauthorizedAccessException)
        {
          // Skip directories we can't access
        }
      }

      int totalAdded = 0;
      foreach (string file in foundFiles.Distinct())
      {
        try
        {
          List<McpServer> importedServers = await _configImportService.ImportFromClaudeCodeAsync(file);
          totalAdded += AddImportedServers(importedServers);
        }
        catch
        {
          // Skip files that can't be parsed
        }
      }

      StatusMessage = $"Found {foundFiles.Count} config files, imported {totalAdded} servers";
    }
    catch (Exception ex)
    {
      StatusMessage = $"Scan error: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  private int AddImportedServers(List<McpServer> importedServers)
  {
    if (_registry == null)
    {
      return 0;
    }

    int addedCount = 0;
    foreach (McpServer server in importedServers)
    {
      // Check if server with same name exists
      if (_registry.Servers.Any(s => s.Id == server.Id || s.Name == server.Name))
      {
        // Skip duplicates
        continue;
      }

      _registry.Servers.Add(server);
      McpServerViewModel cdVm = new(server);
      cdVm.PropertyChanged += OnServerPropertyChanged;
      Servers.Add(cdVm);
      addedCount++;
    }

    // Refresh target folder server lists
    foreach (TargetFolderViewModel target in TargetFolders)
    {
      target.RefreshServers(_registry.Servers);
    }

    return addedCount;
  }

  #endregion

  #region Save & Export Commands

  [RelayCommand]
  private async Task SaveAsync()
  {
    if (_registry == null)
    {
      return;
    }

    IsLoading = true;
    StatusMessage = "Saving...";

    try
    {
      // Update registry from view models
      foreach (McpServerViewModel serverVm in Servers)
      {
        serverVm.UpdateModel();
      }

      foreach (TargetFolderViewModel targetVm in TargetFolders)
      {
        await targetVm.RefreshExistingServersAsync(_registry.Settings);
        targetVm.UpdateModel();
      }

      await _registryService.SaveAsync(_registry);
      StatusMessage = "Saved successfully";
    }
    catch (Exception ex)
    {
      StatusMessage = $"Error saving: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  [RelayCommand]
  private async Task ExportSelectedTargetAsync()
  {
    TargetFolderViewModel? selectedTarget = SelectedTarget;
    if (selectedTarget == null || _registry == null)
    {
      return;
    }

    // First save to sync view models
    foreach (McpServerViewModel serverVm in Servers)
    {
      serverVm.UpdateModel();
    }

    IsLoading = true;

    try
    {
      await selectedTarget.RefreshExistingServersAsync(_registry.Settings);
      selectedTarget.UpdateModel();
      TargetFolder? target = _registry.TargetFolders.FirstOrDefault(t => t.Id == selectedTarget.Id);
      if (target == null)
      {
        return;
      }

      StatusMessage = "Preparing export...";
      Dictionary<string, string>? configs = await _configExportService.PrepareExportAsync(
        [target], _registry.Servers, _registry.Settings, ResolveExportConflictAsync);
      if (configs == null)
      {
        StatusMessage = "Export cancelled. No configuration files were changed.";
        return;
      }

      if (target.IsClipboard)
      {
        // For clipboard targets, copy the config to clipboard
        StatusMessage = "Copying to clipboard...";
        string clipboardText = string.Join("\n\n", configs.Values);

        IClipboard? clipboard = GetMainWindow()?.Clipboard;
        if (clipboard != null)
        {
          await clipboard.SetTextAsync(clipboardText);
          StatusMessage = $"Copied {configs.Count} config(s) to clipboard";
        }
      }
      else
      {
        StatusMessage = $"Exporting to {selectedTarget.Path}...";
        await _configExportService.WriteConfigsAsync(configs);
        await selectedTarget.RefreshExistingServersAsync(_registry.Settings);
        StatusMessage = $"Exported configs to {selectedTarget.Path}";
      }
    }
    catch (Exception ex)
    {
      StatusMessage = $"Error exporting: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  [RelayCommand]
  private async Task ExportAllTargetsAsync()
  {
    if (_registry == null)
    {
      return;
    }

    // First save to sync view models
    foreach (McpServerViewModel serverVm in Servers)
    {
      serverVm.UpdateModel();
    }

    foreach (TargetFolderViewModel targetVm in TargetFolders)
    {
      await targetVm.RefreshExistingServersAsync(_registry.Settings);
      targetVm.UpdateModel();
    }

    IsLoading = true;
    try
    {
      List<TargetFolder> targets = _registry.TargetFolders.Where(t => !t.IsClipboard).ToList();
      StatusMessage = "Preparing exports...";
      Dictionary<string, string>? configs = await _configExportService.PrepareExportAsync(
        targets, _registry.Servers, _registry.Settings, ResolveExportConflictAsync);
      if (configs == null)
      {
        StatusMessage = "Export cancelled. No configuration files were changed.";
        return;
      }

      StatusMessage = "Writing configurations...";
      await _configExportService.WriteConfigsAsync(configs);
      foreach (TargetFolderViewModel targetVm in TargetFolders)
      {
        await targetVm.RefreshExistingServersAsync(_registry.Settings);
      }

      StatusMessage = $"Exported to {targets.Count} targets";
    }
    catch (Exception ex)
    {
      StatusMessage = $"Error exporting: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  private async Task<ExportConflictResolution?> ResolveExportConflictAsync(ExportConflict conflict)
  {
    Window? window = GetMainWindow();
    if (window == null)
    {
      return null;
    }

    StatusMessage = $"Export paused: '{conflict.ServerName}' is not owned by MCP Manager.";
    Views.ExportConflictDialog dialog = new() { DataContext = conflict };
    return await dialog.ShowDialog<ExportConflictResolution?>(window);
  }

  #endregion

  #region File Browser Commands

  [RelayCommand]
  private async Task BrowseWorkingDirectoryAsync()
  {
    if (SelectedServer == null)
    {
      return;
    }

    Window? window = GetMainWindow();
    if (window == null)
    {
      return;
    }

    IReadOnlyList<IStorageFolder> folders = await window.StorageProvider.OpenFolderPickerAsync(
      new FolderPickerOpenOptions
      {
        Title = "Select Working Directory",
        AllowMultiple = false,
      });

    if (folders.Count > 0)
    {
      if (SelectedServer.IsStdio)
      {
        SelectedServer.WorkingDirectory = folders[0].Path.LocalPath;
      }
      else
      {
        SelectedServer.StartupWorkingDirectory = folders[0].Path.LocalPath;
      }
    }
  }

  [RelayCommand]
  private async Task OpenSponsorLinkAsync()
  {
    try
    {
      if (Avalonia.Application.Current?.ApplicationLifetime
          is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
          && desktop.MainWindow is { } window)
      {
        Avalonia.Controls.TopLevel? topLevel = Avalonia.Controls.TopLevel.GetTopLevel(window);
        if (topLevel?.Launcher is { } launcher)
        {
          await launcher.LaunchUriAsync(new Uri("https://ko-fi.com/frankhommers"));
        }
      }
    }
    catch
    {
    }
  }

  #endregion
}
