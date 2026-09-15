using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.ViewModels;

public partial class TargetFolderViewModel : ViewModelBase
{
  private readonly TargetFolder _model;
  private readonly TargetConfigSelectionService _configSelectionService;
  private readonly Dictionary<Guid, bool> _selectionOverrides = [];
  private HashSet<Guid> _existingEnabledServerIds = [];
  private bool _applyingExistingSelections;
  private int _selectionVersion;

  public Guid Id => _model.Id;

  [ObservableProperty] private string _name;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(CanReadConfig))]
  private string _path;

  [ObservableProperty] private bool _isGlobal;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(CanReadConfig))]
  private bool _isClipboard;

  [ObservableProperty] private bool _isQuickExport;

  [ObservableProperty] private bool _enableClaudeCode;

  [ObservableProperty] private bool _enableClaudeDesktop;

  [ObservableProperty] private bool _enableOpenCode;

  [ObservableProperty] private bool _enableCodex;

  [ObservableProperty] private string _existingServersStatus = string.Empty;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(CanReadConfig))]
  private bool _isReadingConfig;

  [ObservableProperty] private string _configReadStatus = "Config has not been checked yet.";

  public bool CanReadConfig => !IsClipboard && !IsReadingConfig && !string.IsNullOrWhiteSpace(Path);

  [ObservableProperty] private string _bridgeArgs = string.Empty;

  /// <summary>
  /// True when this is the global Codex CLI target.
  /// </summary>
  public bool IsCodex => IsGlobal && _model.EnabledClients.HasFlag(TargetClientFlags.Codex);

  /// <summary>
  /// True when this is the global Claude Desktop target.
  /// </summary>
  public bool IsClaudeDesktopGlobal => IsGlobal && _model.EnabledClients.HasFlag(TargetClientFlags.ClaudeDesktop);

  public bool IsCursor => IsGlobal && _model.EnabledClients.HasFlag(TargetClientFlags.Cursor);

  public bool IsWindsurf => IsGlobal && _model.EnabledClients.HasFlag(TargetClientFlags.Windsurf);

  public bool IsVsCode => IsGlobal && _model.EnabledClients.HasFlag(TargetClientFlags.VsCode);

  public bool IsOpenCode => IsGlobal && _model.EnabledClients.HasFlag(TargetClientFlags.OpenCode);

  public bool IsClaudeCodeGlobal => IsGlobal && _model.EnabledClients.HasFlag(TargetClientFlags.ClaudeCodeGlobal);

  /// <summary>
  /// Selected clipboard format as string for RadioButton binding.
  /// Maps to/from EnabledClients flags ensuring exactly one is set.
  /// </summary>
  public string SelectedClipboardFormat
  {
    get
    {
      if (EnableClaudeDesktop) return "ClaudeDesktop";
      if (EnableOpenCode) return "OpenCode";
      return "ClaudeCode";
    }
    set
    {
      EnableClaudeCode = value == "ClaudeCode";
      EnableClaudeDesktop = value == "ClaudeDesktop";
      EnableOpenCode = value == "OpenCode";
      EnableCodex = false;
      OnPropertyChanged();
    }
  }

  /// <summary>
  /// Full path to the config file for display purposes.
  /// </summary>
  public string ConfigFilePath
  {
    get
    {
      TargetClientFlags clients = GetEnabledClients();
      if (clients.HasFlag(TargetClientFlags.Codex))
        return IsGlobal
          ? System.IO.Path.Combine(Path, "config.toml")
          : System.IO.Path.Combine(Path, ".codex", "config.toml");
      if (clients.HasFlag(TargetClientFlags.ClaudeDesktop))
        return System.IO.Path.Combine(Path, "claude_desktop_config.json");
      if (clients.HasFlag(TargetClientFlags.ClaudeCodeGlobal))
        return System.IO.Path.Combine(Path, ".claude.json");
      if (clients.HasFlag(TargetClientFlags.ClaudeCode))
        return System.IO.Path.Combine(Path, ".mcp.json");
      if (clients.HasFlag(TargetClientFlags.Cursor))
        return System.IO.Path.Combine(Path, ".cursor", "mcp.json");
      if (clients.HasFlag(TargetClientFlags.Windsurf))
        return System.IO.Path.Combine(Path, "mcp_config.json");
      if (clients.HasFlag(TargetClientFlags.VsCode))
        return System.IO.Path.Combine(Path, ".vscode", "mcp.json");
      if (clients.HasFlag(TargetClientFlags.OpenCode))
        return System.IO.Path.Combine(Path, "opencode.jsonc");
      return Path;
    }
  }

  [ObservableProperty] private ObservableCollection<ServerSelectionViewModel> _serverSelections = [];

  public TargetFolderViewModel(
    TargetFolder model, List<McpServer> allServers, IConfigExportService? exportService = null)
  {
    _model = model;
    _configSelectionService = new TargetConfigSelectionService(exportService ?? new ConfigExportService());
    foreach (Guid id in model.EnabledServers)
    {
      _selectionOverrides[id] = true;
    }

    foreach (Guid id in model.DisabledServers)
    {
      _selectionOverrides[id] = false;
    }

    _name = model.Name;
    _path = model.Path;
    _isGlobal = model.IsGlobal;
    _isClipboard = model.IsClipboard;
    _isQuickExport = model.IsQuickExport;
    _enableClaudeCode = model.EnabledClients.HasFlag(TargetClientFlags.ClaudeCode);
    _enableClaudeDesktop = model.EnabledClients.HasFlag(TargetClientFlags.ClaudeDesktop);
    _enableOpenCode = model.EnabledClients.HasFlag(TargetClientFlags.OpenCode);
    _enableCodex = model.EnabledClients.HasFlag(TargetClientFlags.Codex);
    _bridgeArgs = model.BridgeArgs;
    ServerSelections.CollectionChanged += OnSelectionCollectionChanged;

    // Build server selection list
    foreach (McpServer server in allServers)
    {
      ServerSelectionViewModel selection = new()
      {
        ServerId = server.Id,
        ServerName = string.IsNullOrWhiteSpace(server.DisplayName) ? server.Name : server.DisplayName,
        ServerKey = server.Name,
        Group = server.Group,
        TransportType = server.TransportType,
        IsEnabled = model.EnabledServers.Contains(server.Id) && !model.DisabledServers.Contains(server.Id),
        IsDisabled = model.DisabledServers.Contains(server.Id),
      };

      BuildToolOverrides(selection, server, model.ServerToolOverrides);
      selection.PropertyChanged += OnServerSelectionChanged;
      ServerSelections.Add(selection);
    }
  }

  public async Task<ExistingTargetServers?> RefreshExistingServersAsync(GlobalSettings? settings = null, bool debounce = false)
  {
    int version = ++_selectionVersion;
    IsReadingConfig = true;
    ConfigReadStatus = "Reading config…";
    try
    {
      ExistingTargetServers? result = await ReadExistingServersAsync(version, settings, debounce);
      if (result == null && version == _selectionVersion)
      {
        ConfigReadStatus = "Config changed during the check. Re-read config to check again.";
      }

      return result;
    }
    catch
    {
      if (version == _selectionVersion)
      {
        ConfigReadStatus = "Config check failed.";
      }

      throw;
    }
    finally
    {
      if (version == _selectionVersion)
      {
        IsReadingConfig = false;
      }
    }
  }

  private async Task<ExistingTargetServers?> ReadExistingServersAsync(
    int version, GlobalSettings? settings, bool debounce)
  {
    if (debounce)
    {
      await Task.Delay(250);
      if (version != _selectionVersion)
      {
        return null;
      }
    }

    TargetFolder target = new()
    {
      Path = Path,
      IsGlobal = IsGlobal,
      IsClipboard = IsClipboard,
      EnabledClients = GetEnabledClients(),
    };
    ExistingTargetServers existing = await _configSelectionService.ReadAsync(target, settings);
    if (version != _selectionVersion || target.Path != Path || target.EnabledClients != GetEnabledClients() ||
        target.IsGlobal != IsGlobal || target.IsClipboard != IsClipboard)
    {
      return null;
    }

    _existingServers = existing.Servers;
    RefreshFoundServers();

    if (existing.UnreadableFiles.Count > 0)
    {
      _existingEnabledServerIds.UnionWith(existing.EnabledServerIds);
    }
    else
    {
      _existingEnabledServerIds = existing.EnabledServerIds;
    }

    ApplyExistingSelections();
    List<string> messages = [];
    if (existing.ConfigFileCount > 0)
    {
      int matched = ServerSelections.Count(s => existing.EnabledServerIds.Contains(s.ServerId));
      messages.Add($"Read {existing.Servers.Count} server entries from {existing.ConfigFileCount} config file(s). " +
                   $"{matched} enabled servers match your library.");
      int unknown = existing.EnabledServerIds.Count - matched;
      if (unknown > 0)
      {
        messages.Add($"{unknown} managed servers are missing from the library. Import them before exporting to keep them.");
      }
    }

    if (existing.UnmanagedServerCount > 0)
    {
      messages.Add($"Not owned by MCP Manager: {existing.UnmanagedServerCount} existing server(s). " +
                   "These are not selected automatically. Resolve any name conflicts on export.");
    }

    if (existing.UnreadableFiles.Count > 0)
    {
      messages.Add($"Could not read: {string.Join(", ", existing.UnreadableFiles)}. Existing selections were kept.");
    }

    if (existing.ConfigFileCount == 0 && existing.UnreadableFiles.Count == 0 && !IsClipboard)
    {
      messages.Add("No configuration files found for this target.");
    }

    ExistingServersStatus = string.Join(" ", messages);
    ConfigReadStatus = $"Last checked: {DateTimeOffset.Now:yyyy-MM-ddTHH:mm:sszzz}";
    return existing;
  }

  private void ApplyExistingSelections()
  {
    _applyingExistingSelections = true;
    try
    {
      foreach (ServerSelectionViewModel selection in ServerSelections)
      {
        selection.IsEnabled = _selectionOverrides.TryGetValue(selection.ServerId, out bool enabled)
          ? enabled
          : _existingEnabledServerIds.Contains(selection.ServerId);
      }
    }
    finally
    {
      _applyingExistingSelections = false;
    }
  }

  private void OnServerSelectionChanged(object? sender, PropertyChangedEventArgs e)
  {
    if (!_applyingExistingSelections && e.PropertyName == nameof(ServerSelectionViewModel.IsEnabled) &&
        sender is ServerSelectionViewModel selection)
    {
      _selectionOverrides[selection.ServerId] = selection.IsEnabled;
      selection.IsDisabled = !selection.IsEnabled;
    }

    if (e.PropertyName == nameof(ServerSelectionViewModel.IsEnabled))
    {
      OnPropertyChanged(nameof(ServerSelectionSummary));
    }
    else if (e.PropertyName is nameof(ServerSelectionViewModel.ServerName)
             or nameof(ServerSelectionViewModel.ServerKey) or nameof(ServerSelectionViewModel.Group))
    {
      RefreshServerList();
    }
  }

  private TargetClientFlags GetEnabledClients()
  {
    if (IsGlobal)
    {
      return _model.EnabledClients;
    }

    TargetClientFlags clients = TargetClientFlags.None;
    if (EnableClaudeCode) clients |= TargetClientFlags.ClaudeCode;
    if (EnableClaudeDesktop) clients |= TargetClientFlags.ClaudeDesktop;
    if (EnableOpenCode) clients |= TargetClientFlags.OpenCode;
    if (EnableCodex) clients |= TargetClientFlags.Codex;
    return clients;
  }

  public void RefreshServers(List<McpServer> allServers)
  {
    // Add new servers (check model's EnabledServers for enabled state)
    foreach (McpServer server in allServers)
    {
      if (!ServerSelections.Any(s => s.ServerId == server.Id))
      {
        if (_model.EnabledServers.Contains(server.Id) || _model.DisabledServers.Contains(server.Id))
        {
          _selectionOverrides.TryAdd(server.Id, !_model.DisabledServers.Contains(server.Id));
        }

        ServerSelectionViewModel selection = new()
        {
          ServerId = server.Id,
          ServerName = string.IsNullOrWhiteSpace(server.DisplayName) ? server.Name : server.DisplayName,
          ServerKey = server.Name,
          Group = server.Group,
          TransportType = server.TransportType,
          IsEnabled = _model.EnabledServers.Contains(server.Id),
          IsDisabled = _model.DisabledServers.Contains(server.Id),
        };

        BuildToolOverrides(selection, server, _model.ServerToolOverrides);
        selection.PropertyChanged += OnServerSelectionChanged;
        ServerSelections.Add(selection);
      }
    }

    // Update names and tool overrides for existing
    foreach (ServerSelectionViewModel selection in ServerSelections)
    {
      McpServer? server = allServers.FirstOrDefault(s => s.Id == selection.ServerId);
      if (server != null)
      {
        selection.ServerName = string.IsNullOrWhiteSpace(server.DisplayName) ? server.Name : server.DisplayName;
        selection.ServerKey = server.Name;
        selection.Group = server.Group;
        selection.TransportType = server.TransportType;
        RefreshToolOverrides(selection, server);
      }
    }

    // Remove deleted servers
    List<ServerSelectionViewModel> toRemove =
      ServerSelections.Where(s => !allServers.Any(srv => srv.Id == s.ServerId)).ToList();
    foreach (ServerSelectionViewModel item in toRemove)
    {
      item.PropertyChanged -= OnServerSelectionChanged;
      ServerSelections.Remove(item);
    }

    ApplyExistingSelections();
  }

  public void UpdateModel()
  {
    _model.Name = Name;
    _model.Path = Path;
    _model.IsGlobal = IsGlobal;
    _model.IsClipboard = IsClipboard;
    _model.IsQuickExport = IsQuickExport;
    _model.BridgeArgs = BridgeArgs;

    _model.EnabledClients = GetEnabledClients();

    _model.EnabledServers.Clear();
    _model.DisabledServers.Clear();
    _model.ServerToolOverrides.Clear();

    foreach (ServerSelectionViewModel selection in ServerSelections)
    {
      if (selection.IsEnabled)
      {
        _model.EnabledServers.Add(selection.ServerId);
      }
      else if (selection.IsDisabled)
      {
        _model.DisabledServers.Add(selection.ServerId);
      }

      if (selection.HasToolOverrides)
      {
        List<string> allowedTools = selection.ToolOverrides
          .Where(t => t.IsAllowed)
          .Select(t => t.ToolName)
          .ToList();
        _model.ServerToolOverrides[selection.ServerId] = allowedTools;
      }
    }
  }
  private static void BuildToolOverrides(
    ServerSelectionViewModel selection,
    McpServer server,
    Dictionary<Guid, List<string>> serverToolOverrides)
  {
    HashSet<string> allToolNames = new(server.KnownTools);
    foreach (string tool in server.AlwaysAllow)
    {
      allToolNames.Add(tool);
    }

    if (allToolNames.Count == 0)
    {
      return;
    }

    // Determine which tools are allowed: use override if present, else server default
    HashSet<string> allowedSet;
    if (serverToolOverrides.TryGetValue(server.Id, out List<string>? overrides))
    {
      allowedSet = new HashSet<string>(overrides);
    }
    else
    {
      allowedSet = new HashSet<string>(server.AlwaysAllow);
    }

    foreach (string name in allToolNames.OrderBy(n => n))
    {
      selection.ToolOverrides.Add(
        new ToolOverrideViewModel
        {
          ToolName = name,
          IsAllowed = allowedSet.Contains(name),
        });
    }

    selection.NotifyToolOverridesChanged();
  }

  private static void RefreshToolOverrides(
    ServerSelectionViewModel selection,
    McpServer server)
  {
    HashSet<string> allToolNames = new(server.KnownTools);
    foreach (string tool in server.AlwaysAllow)
    {
      allToolNames.Add(tool);
    }

    if (allToolNames.Count == 0)
    {
      if (selection.ToolOverrides.Count > 0)
      {
        selection.ToolOverrides.Clear();
        selection.NotifyToolOverridesChanged();
      }

      return;
    }

    // Preserve existing allowed state for tools that still exist
    Dictionary<string, bool> existing = selection.ToolOverrides.ToDictionary(t => t.ToolName, t => t.IsAllowed);

    // Add new tools (default to allowed if in server's AlwaysAllow)
    HashSet<string> serverDefaults = new(server.AlwaysAllow);
    foreach (string name in allToolNames.OrderBy(n => n))
    {
      if (!existing.ContainsKey(name))
      {
        selection.ToolOverrides.Add(
          new ToolOverrideViewModel
          {
            ToolName = name,
            IsAllowed = serverDefaults.Contains(name),
          });
      }
    }

    // Remove tools that no longer exist
    List<ToolOverrideViewModel> toRemove =
      selection.ToolOverrides.Where(t => !allToolNames.Contains(t.ToolName)).ToList();
    foreach (ToolOverrideViewModel item in toRemove)
    {
      selection.ToolOverrides.Remove(item);
    }

    selection.NotifyToolOverridesChanged();
  }
}

public partial class ServerSelectionViewModel : ViewModelBase
{
  public Guid ServerId { get; set; }

  [ObservableProperty] private string _serverName = string.Empty;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(Details))]
  private string _serverKey = string.Empty;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(Details))]
  private string _group = string.Empty;

  [ObservableProperty] private McpTransportType _transportType;

  public string Details => string.IsNullOrWhiteSpace(Group) ? ServerKey : $"{Group} · {ServerKey}";

  [ObservableProperty] private bool _isEnabled;

  [ObservableProperty] private bool _isDisabled;

  [ObservableProperty] private ObservableCollection<ToolOverrideViewModel> _toolOverrides = [];

  public bool HasToolOverrides => ToolOverrides.Count > 0;

  public void NotifyToolOverridesChanged()
  {
    RefreshTools();
  }

  public void AllowAllTools()
  {
    foreach (ToolOverrideViewModel tool in ToolOverrides)
    {
      tool.IsAllowed = true;
    }
  }

  public void DenyAllTools()
  {
    foreach (ToolOverrideViewModel tool in ToolOverrides)
    {
      tool.IsAllowed = false;
    }
  }
}
