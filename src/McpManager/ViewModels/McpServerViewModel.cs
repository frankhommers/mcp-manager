using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

public partial class McpServerViewModel : ViewModelBase
{
  private readonly McpServer _model;

  public Guid Id => _model.Id;

  [ObservableProperty] private string _name;

  [ObservableProperty] private string _displayName;

  [ObservableProperty] private string? _description;

  [ObservableProperty] private string _group = string.Empty;

  [ObservableProperty] private McpTransportType _transportType;

  // Stdio fields
  [ObservableProperty] private string? _command;

  [ObservableProperty] private string _argumentsText = string.Empty;

  [ObservableProperty] private string? _workingDirectory;

  // HTTP fields
  [ObservableProperty] private string? _url;

  // Startup command (for HTTP servers)
  [ObservableProperty] private string? _startupCommand;

  [ObservableProperty] private string? _startupWorkingDirectory;

  // Environment
  [ObservableProperty] private string _environmentText = string.Empty;

  // HTTP Headers (for remote servers)
  [ObservableProperty] private string _httpHeadersText = string.Empty;

  // Always Allow tools
  [ObservableProperty] private ObservableCollection<ToolSelectionViewModel> _toolSelections = [];

  public bool HasTools => ToolSelections.Count > 0;

  public IEnumerable<TransportTypeItem> TransportTypeItems => TransportTypeItem.All;

  public TransportTypeItem SelectedTransportTypeItem
  {
    get => TransportTypeItem.All.First(t => t.Value == TransportType);
    set
    {
      if (value != null && TransportType != value.Value)
      {
        TransportType = value.Value;
        OnPropertyChanged();
      }
    }
  }

  // Connection Mode: Local Command vs Remote URL
  public IEnumerable<ConnectionModeItem> ConnectionModes => ConnectionModeItem.All;

  public ConnectionModeItem SelectedConnectionMode
  {
    get => ConnectionModeItem.All.First(m => m.IsRemote == IsHttp);
    set
    {
      if (value != null)
      {
        if (value.IsRemote && TransportType == McpTransportType.Stdio)
        {
          // Switching to Remote URL - default to SSE
          TransportType = McpTransportType.Sse;
        }
        else if (!value.IsRemote && TransportType != McpTransportType.Stdio)
        {
          // Switching to Local Command
          TransportType = McpTransportType.Stdio;
        }

        OnPropertyChanged();
        OnPropertyChanged(nameof(SelectedHttpProtocol));
      }
    }
  }

  // HTTP Protocol: SSE, Streamable HTTP, HTTP (only for Remote URL)
  public IEnumerable<HttpProtocolItem> HttpProtocols => HttpProtocolItem.All;

  public HttpProtocolItem SelectedHttpProtocol
  {
    get => HttpProtocolItem.All.FirstOrDefault(p => p.Value == TransportType)
           ?? HttpProtocolItem.All.First();
    set
    {
      if (value != null && TransportType != value.Value && IsHttp)
      {
        TransportType = value.Value;
        OnPropertyChanged();
      }
    }
  }

  public bool IsStdio => TransportType == McpTransportType.Stdio;
  public bool IsHttp => TransportType != McpTransportType.Stdio;
  public bool HasStartupCommand => !string.IsNullOrWhiteSpace(StartupCommand);

  public McpServerViewModel(McpServer model)
  {
    _model = model;
    _name = model.Name;
    _displayName = model.DisplayName;
    _description = model.Description;
    _group = model.Group;
    _transportType = model.TransportType;
    _command = model.Command;
    _argumentsText = string.Join(" ", model.Args);
    _workingDirectory = model.WorkingDirectory;
    _url = model.Url;
    _startupCommand = model.StartupCommand;
    _startupWorkingDirectory = model.StartupWorkingDirectory;

    // Convert environment variables to text format
    IEnumerable<string> envLines = model.EnvironmentVariables.Select(kvp => $"{kvp.Key}={kvp.Value}");
    _environmentText = string.Join("\n", envLines);

    // Convert HTTP headers to text format
    IEnumerable<string> headerLines = model.HttpHeaders.Select(kvp => $"{kvp.Key}={kvp.Value}");
    _httpHeadersText = string.Join("\n", headerLines);

    // Load tool selections from cached known tools + always-allow
    HashSet<string> allToolNames = new(model.KnownTools);
    foreach (string tool in model.AlwaysAllow)
    {
      allToolNames.Add(tool);
    }

    HashSet<string> allowedSet = new(model.AlwaysAllow);
    foreach (string name in allToolNames.OrderBy(n => n))
    {
      _toolSelections.Add(
        new ToolSelectionViewModel
        {
          ToolName = name,
          IsAllowed = allowedSet.Contains(name),
        });
    }
  }

  /// <summary>
  /// Populate available tools from a fetched tools list.
  /// Existing always-allow selections are preserved.
  /// </summary>
  public void SetAvailableTools(IEnumerable<string> toolNames)
  {
    Dictionary<string, bool> existing = ToolSelections.ToDictionary(t => t.ToolName, t => t.IsAllowed);
    ToolSelections.Clear();
    foreach (string name in toolNames.OrderBy(n => n))
    {
      ToolSelections.Add(
        new ToolSelectionViewModel
        {
          ToolName = name,
          IsAllowed = existing.GetValueOrDefault(name, false),
        });
    }

    OnPropertyChanged(nameof(HasTools));
  }

  partial void OnTransportTypeChanged(McpTransportType value)
  {
    OnPropertyChanged(nameof(IsStdio));
    OnPropertyChanged(nameof(IsHttp));
    OnPropertyChanged(nameof(SelectedTransportTypeItem));
    OnPropertyChanged(nameof(SelectedConnectionMode));
    OnPropertyChanged(nameof(SelectedHttpProtocol));
  }

  partial void OnStartupCommandChanged(string? value)
  {
    OnPropertyChanged(nameof(HasStartupCommand));
  }

  public void UpdateModel()
  {
    _model.Name = Name;
    _model.DisplayName = DisplayName;
    _model.Description = Description;
    _model.Group = Group;
    _model.TransportType = TransportType;
    _model.Command = Command;
    _model.Args = string.IsNullOrWhiteSpace(ArgumentsText)
      ? []
      : ArgumentsText.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    _model.WorkingDirectory = WorkingDirectory;
    _model.Url = Url;
    _model.StartupCommand = StartupCommand;
    _model.StartupWorkingDirectory = StartupWorkingDirectory;

    // Save always-allow tools and cache all known tool names
    _model.AlwaysAllow = ToolSelections.Where(t => t.IsAllowed).Select(t => t.ToolName).ToList();
    _model.KnownTools = ToolSelections.Select(t => t.ToolName).ToList();

    // Parse environment text into key-value pairs
    _model.EnvironmentVariables.Clear();
    if (!string.IsNullOrWhiteSpace(EnvironmentText))
    {
      string[] lines = EnvironmentText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
      foreach (string line in lines)
      {
        int eqIndex = line.IndexOf('=');
        if (eqIndex > 0)
        {
          string key = line[..eqIndex].Trim();
          string value = line[(eqIndex + 1)..].Trim();
          if (!string.IsNullOrEmpty(key))
          {
            _model.EnvironmentVariables[key] = value;
          }
        }
      }
    }

    // Parse HTTP headers text into key-value pairs
    _model.HttpHeaders.Clear();
    if (!string.IsNullOrWhiteSpace(HttpHeadersText))
    {
      string[] lines = HttpHeadersText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
      foreach (string line in lines)
      {
        int eqIndex = line.IndexOf('=');
        if (eqIndex > 0)
        {
          string key = line[..eqIndex].Trim();
          string value = line[(eqIndex + 1)..].Trim();
          if (!string.IsNullOrEmpty(key))
          {
            _model.HttpHeaders[key] = value;
          }
        }
      }
    }
  }
}