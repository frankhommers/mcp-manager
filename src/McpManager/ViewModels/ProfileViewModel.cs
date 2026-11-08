using System;
using System.Collections.Generic;
using McpManager.Core.Models;

namespace McpManager.ViewModels;

public class ProfileViewModel : TargetFolderViewModel
{
  public McpProfile Profile { get; }

  public ProfileViewModel(McpProfile profile, List<McpServer> servers)
    : base(new TargetFolder
    {
      Id = profile.Id,
      Name = profile.Name,
      IsClipboard = true,
      EnabledServers = new(profile.EnabledServers),
      ServerToolOverrides = new(profile.ServerToolOverrides),
    }, servers)
  {
    Profile = profile;
    ConfigurationChanged += (_, _) => UpdateProfile();
    PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Name)) UpdateProfile(); };
  }

  public void UpdateProfile()
  {
    UpdateModel();
    Profile.Name = Name;
    Profile.EnabledServers = new(Model.EnabledServers);
    Profile.ServerToolOverrides = new(Model.ServerToolOverrides);
  }
}
