using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

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

  [ObservableProperty] private string _bridgeCommandHttp = "mcp-proxy {args} {url}";

  [ObservableProperty] private string _bridgeCommandSse = "mcp-proxy {args} {url}";

  [ObservableProperty]
  private string _bridgeCommandStreamableHttp = "mcp-proxy {args} --transport streamablehttp {url}";

  [ObservableProperty] private string _selectedThemeMode = "Follow system";

  public ObservableCollection<string> ThemeModes { get; } = ["Follow system", "Dark", "Light"];

  public IEnumerable<McpTransportType> TransportTypes => Enum.GetValues<McpTransportType>();

  /// <summary>
  /// Migrates old bridge commands to include {args} placeholder.
  /// Inserts {args} after the command name (e.g., mcp-proxy {url} → mcp-proxy {args} {url})
  /// </summary>
  private static string MigrateBridgeCommand(string command)
  {
    if (string.IsNullOrWhiteSpace(command) || command.Contains("{args}"))
    {
      return command;
    }

    // Insert {args} after the first word (the command)
    string[] parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 2)
    {
      return $"{parts[0]} {{args}} {parts[1]}";
    }

    if (parts.Length == 1)
    {
      return $"{parts[0]} {{args}}";
    }

    return command;
  }

  public MainWindowViewModel()
  {
    _registryService = new RegistryService();
    _configExportService = new ConfigExportService();
    _configImportService = new ConfigImportService();
    _httpMcpTester = new HttpMcpTester();
    _transportDetectionService = new TransportDetectionService();
    _shellEnvironmentService = new ShellEnvironmentService();
  }

  public MainWindowViewModel(
    IRegistryService registryService,
    IConfigExportService configExportService,
    IConfigImportService configImportService,
    IHttpMcpTester httpMcpTester,
    ITransportDetectionService transportDetectionService,
    IShellEnvironmentService shellEnvironmentService)
  {
    _registryService = registryService;
    _configExportService = configExportService;
    _configImportService = configImportService;
    _httpMcpTester = httpMcpTester;
    _transportDetectionService = transportDetectionService;
    _shellEnvironmentService = shellEnvironmentService;
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

      // Load settings and migrate old bridge commands to include {args}
      BridgeCommandHttp = MigrateBridgeCommand(_registry.Settings.BridgeCommandHttp);
      BridgeCommandSse = MigrateBridgeCommand(_registry.Settings.BridgeCommandSse);
      BridgeCommandStreamableHttp = MigrateBridgeCommand(_registry.Settings.BridgeCommandStreamableHttp);

      if (!string.IsNullOrEmpty(_registry.Settings.ThemeMode))
      {
        SelectedThemeMode = _registry.Settings.ThemeMode;
      }

      // Save if migrated
      if (BridgeCommandHttp != _registry.Settings.BridgeCommandHttp ||
          BridgeCommandSse != _registry.Settings.BridgeCommandSse ||
          BridgeCommandStreamableHttp != _registry.Settings.BridgeCommandStreamableHttp)
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

    TargetFolders.Clear();
    foreach (TargetFolder target in _registry.TargetFolders)
    {
      TargetFolders.Add(new TargetFolderViewModel(target, _registry.Servers));
    }

    OnPropertyChanged(nameof(ServerGroups));
  }

  private void OnServerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
  {
    // Refresh tree when display-relevant properties change
    if (e.PropertyName is nameof(McpServerViewModel.DisplayName) or nameof(McpServerViewModel.Group)
        or nameof(McpServerViewModel.Name) or nameof(McpServerViewModel.TransportType))
    {
      OnPropertyChanged(nameof(ServerGroups));
    }

    // Update server name in target checkboxes
    if (e.PropertyName is nameof(McpServerViewModel.DisplayName) && sender is McpServerViewModel serverVm)
    {
      foreach (TargetFolderViewModel target in TargetFolders)
      {
        ServerSelectionViewModel? selection = target.ServerSelections.FirstOrDefault(s => s.ServerId == serverVm.Id);
        if (selection != null)
        {
          selection.ServerName = serverVm.DisplayName;
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
      // Extract the command name from the bridge command (first word before space or {url})
      string bridgeCmd = BridgeCommandHttp.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ??
                         "mcp-proxy";

      // Try to run --version or --help to check if it exists
      string shell = OperatingSystem.IsWindows() ? "cmd" : "/bin/bash";
      string shellArg = OperatingSystem.IsWindows() ? "/c" : "-c";
      string checkCmd = OperatingSystem.IsWindows() ? $"where {bridgeCmd}" : $"which {bridgeCmd}";

      BufferedCommandResult result = await Cli.Wrap(shell)
        .WithArguments([shellArg, checkCmd])
        .WithValidation(CommandResultValidation.None)
        .ExecuteBufferedAsync();

      if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StandardOutput))
      {
        string path = result.StandardOutput.Trim().Split('\n').First();

        // Try to get version
        BufferedCommandResult versionResult = await Cli.Wrap(shell)
          .WithArguments([shellArg, $"{bridgeCmd} --version"])
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

  [RelayCommand]
  private async Task FetchToolsAsync()
  {
    if (SelectedServer == null)
    {
      return;
    }

    IsLoading = true;
    FetchToolsResult = "";
    StatusMessage = "Fetching tools from server...";

    try
    {
      List<string> toolNames = await FetchToolNamesAsync();

      if (toolNames.Count > 0)
      {
        SelectedServer.SetAvailableTools(toolNames);
        FetchToolsResult = $"Found {toolNames.Count} tools: {string.Join(", ", toolNames)}";
        StatusMessage = $"Found {toolNames.Count} tools";
      }
      else
      {
        FetchToolsResult = "No tools found or server did not respond.";
        StatusMessage = "No tools found";
      }
    }
    catch (Exception ex)
    {
      string details = ex.InnerException != null
        ? $"Error: {ex.Message}\nInner: {ex.InnerException.Message}"
        : $"Error: {ex.Message}";
      string path = Environment.GetEnvironmentVariable("PATH") ?? "(not set)";
      FetchToolsResult = $"{details}\n\nPATH: {path}";
      StatusMessage = $"Fetch tools error: {ex.Message}";
    }
    finally
    {
      IsLoading = false;
    }
  }

  private async Task<List<string>> FetchToolNamesAsync()
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

    return await FetchToolNamesForServerAsync(server);
  }

  private async Task<List<string>> FetchToolNamesForServerAsync(McpServer server)
  {
    using CancellationTokenSource cts = new(TimeSpan.FromSeconds(15));

    IClientTransport transport;
    if (server.TransportType == McpTransportType.Stdio)
    {
      if (string.IsNullOrEmpty(server.Command))
      {
        return [];
      }

      Dictionary<string, string?> envVars = server.EnvironmentVariables
        .ToDictionary(kv => kv.Key, kv => (string?)kv.Value);

      transport = new StdioClientTransport(new StdioClientTransportOptions
      {
        Name = server.DisplayName,
        Command = server.Command,
        Arguments = server.Args,
        EnvironmentVariables = envVars.Count > 0 ? envVars : null,
      });
    }
    else
    {
      if (string.IsNullOrEmpty(server.Url))
      {
        return [];
      }

      HttpTransportMode mode = server.TransportType switch
      {
        McpTransportType.Sse => HttpTransportMode.Sse,
        McpTransportType.StreamableHttp => HttpTransportMode.StreamableHttp,
        _ => HttpTransportMode.AutoDetect,
      };

      transport = new HttpClientTransport(new HttpClientTransportOptions
      {
        Endpoint = new Uri(server.Url),
        TransportMode = mode,
      });
    }

    McpClient? client = null;
    try
    {
      client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);
      IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: cts.Token);
      return tools.Select(t => t.Name).ToList();
    }
    finally
    {
      if (client != null)
      {
        try
        {
          await client.DisposeAsync();
        }
        catch
        {
          // Swallow dispose errors from MCP SDK
        }
      }
    }
  }

  private static Dictionary<string, string?> ParseEnvironmentText(string? environmentText)
  {
    Dictionary<string, string?> envVars = new();
    if (string.IsNullOrWhiteSpace(environmentText))
    {
      return envVars;
    }

    string[] lines = environmentText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    foreach (string line in lines)
    {
      int eqIndex = line.IndexOf('=');
      if (eqIndex > 0)
      {
        string key = line[..eqIndex].Trim();
        string value = line[(eqIndex + 1)..].Trim();
        if (!string.IsNullOrEmpty(key))
        {
          envVars[key] = value;
        }
      }
    }

    return envVars;
  }

  [RelayCommand]
  private async Task FetchAllTargetToolsAsync()
  {
    if (SelectedTarget == null || _registry == null)
    {
      return;
    }

    IsLoading = true;
    StatusMessage = "Fetching tools for target servers...";
    int totalFound = 0;

    try
    {
      // Get enabled servers that don't have tools yet
      List<ServerSelectionViewModel> serversToFetch = SelectedTarget.ServerSelections
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
        }
        catch
        {
          // Skip servers that fail, continue with others
        }
      }

      // Refresh target's tool overrides for all servers
      SelectedTarget.RefreshServers(_registry.Servers);

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
    if (selection == null || _registry == null || SelectedTarget == null)
    {
      return;
    }

    McpServer? server = _registry.Servers.FirstOrDefault(s => s.Id == selection.ServerId);
    if (server == null)
    {
      return;
    }

    IsLoading = true;
    StatusMessage = $"Fetching tools from {server.DisplayName}...";

    try
    {
      List<string> toolNames = await FetchToolNamesForServerAsync(server);
      if (toolNames.Count > 0)
      {
        server.KnownTools = toolNames;

        McpServerViewModel? serverVm = Servers.FirstOrDefault(s => s.Id == server.Id);
        serverVm?.SetAvailableTools(toolNames);

        SelectedTarget.RefreshServers(_registry.Servers);
        StatusMessage = $"Found {toolNames.Count} tools for {server.DisplayName}";
      }
      else
      {
        StatusMessage = $"No tools found for {server.DisplayName}";
      }
    }
    catch (Exception ex)
    {
      StatusMessage = $"Failed to fetch tools from {server.DisplayName}: {ex.Message}";
    }
    finally
    {
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

    IsLoading = true;
    DetectResult = "";
    StatusMessage = "Detecting transport type (sending MCP initialize)...";

    try
    {
      TransportDetectionResult result = await _transportDetectionService.DetectTransportTypeAsync(SelectedServer.Url);

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

    IsLoading = true;
    McpTestResult = "";

    if (SelectedServer.IsStdio)
    {
      // For stdio, run the command directly
      if (string.IsNullOrWhiteSpace(SelectedServer.Command))
      {
        McpTestResult = "❌ No command configured";
        StatusMessage = "Test failed: no command";
        IsLoading = false;
        return;
      }

      StatusMessage = "Testing stdio command directly...";
      string? fullCmd = string.IsNullOrEmpty(SelectedServer.ArgumentsText)
        ? SelectedServer.Command
        : $"{SelectedServer.Command} {SelectedServer.ArgumentsText}";

      await TestViaCommandAsync(SelectedServer.Command, SelectedServer.ArgumentsText ?? "", $"Direct stdio: {fullCmd}");
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
      await TestHttpDirectAsync(SelectedServer.Url, SelectedServer.TransportType);
    }
  }

  [RelayCommand]
  private async Task TestMcpBridgeAsync()
  {
    if (SelectedServer == null)
    {
      return;
    }

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

    // Replace placeholders
    string resolvedCommand = bridgeCommand
      .Replace("{url}", SelectedServer.Url)
      .Replace("{args}", "");

    // Clean up multiple spaces
    while (resolvedCommand.Contains("  "))
    {
      resolvedCommand = resolvedCommand.Replace("  ", " ");
    }

    resolvedCommand = resolvedCommand.Trim();

    string[] parts = resolvedCommand.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    string command = parts[0];
    string args = parts.Length > 1 ? parts[1] : "";

    StatusMessage = "Testing via bridge...";
    await TestViaCommandAsync(command, args, $"Bridge: {resolvedCommand}");
  }

  private async Task TestViaCommandAsync(string command, string args, string description)
  {
    // Send MCP initialize request via stdin and read response
    string initRequest =
      """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"MCP Manager Test","version":"1.0.0"}}}""";

    string fullCommand = string.IsNullOrEmpty(args) ? command : $"{command} {args}";

    // Build environment variables for stdio servers
    Dictionary<string, string> envVars = new();
    if (SelectedServer?.IsStdio == true && !string.IsNullOrWhiteSpace(SelectedServer.EnvironmentText))
    {
      string[] lines = SelectedServer.EnvironmentText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
      foreach (string line in lines)
      {
        int eqIndex = line.IndexOf('=');
        if (eqIndex > 0)
        {
          string key = line[..eqIndex].Trim();
          string value = line[(eqIndex + 1)..].Trim();
          if (!string.IsNullOrEmpty(key))
          {
            envVars[key] = value;
          }
        }
      }
    }

    // Build env prefix for shell
    string envPrefix = "";
    if (envVars.Count > 0)
    {
      IEnumerable<string> envParts = envVars.Select(kv => $"{kv.Key}='{kv.Value.Replace("'", "'\\''")}'");
      envPrefix = string.Join(" ", envParts) + " ";
    }

    // Build debug header
    StringBuilder debugInfo = new();
    debugInfo.AppendLine($"🔧 {description}");
    debugInfo.AppendLine();
    debugInfo.AppendLine("📤 Command:");
    debugInfo.AppendLine($"  {envPrefix}{fullCommand}");
    debugInfo.AppendLine();
    debugInfo.AppendLine("📥 Stdin (MCP initialize):");
    debugInfo.AppendLine($"  {initRequest}");
    debugInfo.AppendLine();

    try
    {
      string shell = OperatingSystem.IsWindows() ? "cmd" : "/bin/bash";
      string shellArg = OperatingSystem.IsWindows() ? "/c" : "-c";
      string shellCommand = $"(echo '{initRequest.Replace("'", "'\\''")}'; sleep 5) | {envPrefix}{fullCommand}";

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

  /// <summary>
  /// Test HTTP MCP server directly via HTTP POST (for servers that don't need mcp-proxy)
  /// </summary>
  private async Task TestHttpDirectAsync(string url, McpTransportType transportType)
  {
    HttpMcpTestResult result = await _httpMcpTester.TestInitializeAsync(url, transportType);
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
    TargetFolderViewModel vm = new(result, _registry.Servers);
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
      TargetFolders.Remove(SelectedTarget);
      SelectedTarget = null;
    }
  }

  [RelayCommand]
  private async Task BrowseTargetPathAsync()
  {
    if (SelectedTarget == null)
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
      SelectedTarget.Path = folders[0].Path.LocalPath;
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
      if (_registry.Servers.Any(s => s.Name == server.Name))
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
    if (SelectedTarget == null || _registry == null)
    {
      return;
    }

    // First save to sync view models
    foreach (McpServerViewModel serverVm in Servers)
    {
      serverVm.UpdateModel();
    }

    SelectedTarget.UpdateModel();

    IsLoading = true;

    try
    {
      TargetFolder? target = _registry.TargetFolders.FirstOrDefault(t => t.Id == SelectedTarget.Id);
      if (target == null)
      {
        return;
      }

      if (target.IsClipboard)
      {
        // For clipboard targets, copy the config to clipboard
        StatusMessage = "Copying to clipboard...";
        Dictionary<string, string> configs = _configExportService.PreviewConfigs(
          target,
          _registry.Servers,
          _registry.Settings);
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
        StatusMessage = $"Exporting to {SelectedTarget.Path}...";
        await _configExportService.ExportAsync(target, _registry.Servers, _registry.Settings);
        StatusMessage = $"Exported configs to {SelectedTarget.Path}";
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
      targetVm.UpdateModel();
    }

    IsLoading = true;
    int count = 0;

    try
    {
      foreach (TargetFolder target in _registry.TargetFolders)
      {
        StatusMessage = $"Exporting to {target.Name}...";
        await _configExportService.ExportAsync(target, _registry.Servers, _registry.Settings);
        count++;
      }

      StatusMessage = $"Exported to {count} targets";
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
