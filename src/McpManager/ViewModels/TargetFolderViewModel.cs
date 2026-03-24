using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

public partial class TargetFolderViewModel : ViewModelBase
{
  private readonly TargetFolder _model;
  private readonly List<McpServer> _allServers;

  public Guid Id => _model.Id;

  [ObservableProperty] private string _name;

  [ObservableProperty] private string _path;

  [ObservableProperty] private bool _isGlobal;

  [ObservableProperty] private bool _isClipboard;

  [ObservableProperty] private bool _isQuickExport;

  [ObservableProperty] private bool _enableClaudeCode;

  [ObservableProperty] private bool _enableClaudeDesktop;

  [ObservableProperty] private bool _enableOpenCode;

  [ObservableProperty] private string _bridgeArgs = string.Empty;

  /// <summary>
  /// True when this is the global Codex CLI target.
  /// </summary>
  public bool IsCodex => IsGlobal && _model.EnabledClients.HasFlag(TargetClientFlags.Codex);

  /// <summary>
  /// True when this is the global Claude Desktop target (not Codex).
  /// </summary>
  public bool IsClaudeDesktopGlobal => IsGlobal && !IsCodex && !IsClipboard;

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
      OnPropertyChanged();
    }
  }

  /// <summary>
  /// Full path to the config file for display purposes.
  /// </summary>
  public string ConfigFilePath => IsCodex
    ? System.IO.Path.Combine(Path, "config.toml")
    : IsClaudeDesktopGlobal
      ? System.IO.Path.Combine(Path, "claude_desktop_config.json")
      : Path;

  [ObservableProperty] private ObservableCollection<ServerSelectionViewModel> _serverSelections = [];

  public TargetFolderViewModel(TargetFolder model, List<McpServer> allServers)
  {
    _model = model;
    _allServers = allServers;

    _name = model.Name;
    _path = model.Path;
    _isGlobal = model.IsGlobal;
    _isClipboard = model.IsClipboard;
    _isQuickExport = model.IsQuickExport;
    _enableClaudeCode = model.EnabledClients.HasFlag(TargetClientFlags.ClaudeCode);
    _enableClaudeDesktop = model.EnabledClients.HasFlag(TargetClientFlags.ClaudeDesktop);
    _enableOpenCode = model.EnabledClients.HasFlag(TargetClientFlags.OpenCode);
    _bridgeArgs = model.BridgeArgs;

    // Build server selection list
    foreach (McpServer server in allServers)
    {
      ServerSelectionViewModel selection = new()
      {
        ServerId = server.Id,
        ServerName = server.DisplayName,
        IsEnabled = model.EnabledServers.Contains(server.Id),
        IsDisabled = model.DisabledServers.Contains(server.Id),
      };

      BuildToolOverrides(selection, server, model.ServerToolOverrides);
      ServerSelections.Add(selection);
    }
  }

  public void RefreshServers(List<McpServer> allServers)
  {
    // Add new servers (check model's EnabledServers for enabled state)
    foreach (McpServer server in allServers)
    {
      if (!ServerSelections.Any(s => s.ServerId == server.Id))
      {
        ServerSelectionViewModel selection = new()
        {
          ServerId = server.Id,
          ServerName = server.DisplayName,
          IsEnabled = _model.EnabledServers.Contains(server.Id),
          IsDisabled = _model.DisabledServers.Contains(server.Id),
        };

        BuildToolOverrides(selection, server, _model.ServerToolOverrides);
        ServerSelections.Add(selection);
      }
    }

    // Update names and tool overrides for existing
    foreach (ServerSelectionViewModel selection in ServerSelections)
    {
      McpServer? server = allServers.FirstOrDefault(s => s.Id == selection.ServerId);
      if (server != null)
      {
        selection.ServerName = server.DisplayName;
        RefreshToolOverrides(selection, server);
      }
    }

    // Remove deleted servers
    List<ServerSelectionViewModel> toRemove =
      ServerSelections.Where(s => !allServers.Any(srv => srv.Id == s.ServerId)).ToList();
    foreach (ServerSelectionViewModel item in toRemove)
    {
      ServerSelections.Remove(item);
    }
  }

  public void UpdateModel()
  {
    _model.Name = Name;
    _model.Path = Path;
    _model.IsGlobal = IsGlobal;
    _model.IsClipboard = IsClipboard;
    _model.IsQuickExport = IsQuickExport;
    _model.BridgeArgs = BridgeArgs;

    // Global targets have fixed client flags (set at creation, not user-selectable)
    if (!IsGlobal)
    {
      _model.EnabledClients = TargetClientFlags.None;
      if (EnableClaudeCode)
      {
        _model.EnabledClients |= TargetClientFlags.ClaudeCode;
      }

      if (EnableClaudeDesktop)
      {
        _model.EnabledClients |= TargetClientFlags.ClaudeDesktop;
      }

      if (EnableOpenCode)
      {
        _model.EnabledClients |= TargetClientFlags.OpenCode;
      }
    }

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

  [ObservableProperty] private bool _isEnabled;

  [ObservableProperty] private bool _isDisabled;

  [ObservableProperty] private ObservableCollection<ToolOverrideViewModel> _toolOverrides = [];

  public bool HasToolOverrides => ToolOverrides.Count > 0;

  public void NotifyToolOverridesChanged()
  {
    OnPropertyChanged(nameof(HasToolOverrides));
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