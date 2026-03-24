using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.Views;

public partial class NewTargetDialog : Window
{
  public TargetFolder? Result { get; private set; }

  private readonly HashSet<TargetClientFlags> _existingGlobalTypes;

  public NewTargetDialog() : this([])
  {
  }

  public NewTargetDialog(HashSet<TargetClientFlags> existingGlobalTypes)
  {
    _existingGlobalTypes = existingGlobalTypes;
    InitializeComponent();
    DisableExistingGlobalTypes();
  }

  private void DisableExistingGlobalTypes()
  {
    if (_existingGlobalTypes.Contains(TargetClientFlags.ClaudeDesktop))
    {
      RbClaudeDesktop.IsEnabled = false;
      RbClaudeDesktop.Opacity = 0.4;
    }

    if (_existingGlobalTypes.Contains(TargetClientFlags.Codex))
    {
      RbCodex.IsEnabled = false;
      RbCodex.Opacity = 0.4;
    }

    if (_existingGlobalTypes.Contains(TargetClientFlags.Cursor))
    {
      RbCursor.IsEnabled = false;
      RbCursor.Opacity = 0.4;
    }

    if (_existingGlobalTypes.Contains(TargetClientFlags.Windsurf))
    {
      RbWindsurf.IsEnabled = false;
      RbWindsurf.Opacity = 0.4;
    }

    if (_existingGlobalTypes.Contains(TargetClientFlags.VsCode))
    {
      RbVsCode.IsEnabled = false;
      RbVsCode.Opacity = 0.4;
    }

    if (_existingGlobalTypes.Contains(TargetClientFlags.OpenCode))
    {
      RbOpenCode.IsEnabled = false;
      RbOpenCode.Opacity = 0.4;
    }

    // Select first enabled radio button
    RadioButton[] buttons = [RbClaudeDesktop, RbCodex, RbCursor, RbWindsurf, RbVsCode, RbOpenCode, RbFolder];
    foreach (RadioButton rb in buttons)
    {
      if (rb.IsEnabled)
      {
        rb.IsChecked = true;
        break;
      }
    }
  }

  private string? GetSelectedTag()
  {
    RadioButton[] buttons = [RbClaudeDesktop, RbCodex, RbCursor, RbWindsurf, RbVsCode, RbOpenCode, RbFolder];
    foreach (RadioButton rb in buttons)
    {
      if (rb.IsChecked == true)
      {
        return rb.Tag as string;
      }
    }

    return null;
  }

  private void AddButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
  {
    string? tag = GetSelectedTag();
    if (tag == null)
    {
      return;
    }

    Result = tag switch
    {
      "ClaudeDesktop" => new TargetFolder
      {
        Name = "Claude Desktop",
        Path = System.IO.Path.GetDirectoryName(RegistryService.GetDefaultClaudeDesktopConfigPath()) ?? "",
        EnabledClients = TargetClientFlags.ClaudeDesktop,
        IsGlobal = true,
      },
      "Codex" => new TargetFolder
      {
        Name = "Codex CLI",
        Path = System.IO.Path.GetDirectoryName(RegistryService.GetDefaultCodexConfigPath()) ?? "",
        EnabledClients = TargetClientFlags.Codex,
        IsGlobal = true,
      },
      "Cursor" => new TargetFolder
      {
        Name = "Cursor",
        Path = RegistryService.GetDefaultCursorConfigPath(),
        EnabledClients = TargetClientFlags.Cursor,
        IsGlobal = true,
      },
      "Windsurf" => new TargetFolder
      {
        Name = "Windsurf",
        Path = RegistryService.GetDefaultWindsurfConfigPath(),
        EnabledClients = TargetClientFlags.Windsurf,
        IsGlobal = true,
      },
      "VsCode" => new TargetFolder
      {
        Name = "VS Code",
        Path = RegistryService.GetDefaultVsCodeConfigPath(),
        EnabledClients = TargetClientFlags.VsCode,
        IsGlobal = true,
      },
      "OpenCode" => new TargetFolder
      {
        Name = "OpenCode",
        Path = RegistryService.GetDefaultOpenCodeConfigPath(),
        EnabledClients = TargetClientFlags.OpenCode,
        IsGlobal = true,
      },
      "Folder" => new TargetFolder
      {
        Name = "New Project",
        Path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        EnabledClients = TargetClientFlags.ClaudeCode,
      },
      _ => null,
    };

    Close(Result);
  }

  private void CancelButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
  {
    Close(null);
  }
}
