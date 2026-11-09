using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McpManager.Core.Models;
using McpManager.Core.Services;

namespace McpManager.ViewModels;

public partial class ScanViewModel : ViewModelBase
{
  private static readonly DiffChoice[] WalkthroughChoices =
    [DiffChoice.KeepLibrary, DiffChoice.UseFoundVersion, DiffChoice.ImportSeparately];

  private readonly ConfigScanService _scanService;
  private readonly ScanImportPlanner _planner;
  private readonly Func<McpRegistry?> _getRegistry;
  private readonly Action<int> _applied;
  private List<ScannedFolder> _pendingFolders = [];
  private IReadOnlyList<ScanServerMatch> _pendingMatches = [];
  private List<ScanServerMatch> _differing = [];

  [ObservableProperty]
  [NotifyCanExecuteChangedFor(nameof(AddSelectedCommand))]
  private bool _isScanning;

  [ObservableProperty] private string _message = string.Empty;

  [ObservableProperty] private string _rootsText = string.Empty;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(IsWalkthroughOpen))]
  [NotifyCanExecuteChangedFor(nameof(AddSelectedCommand))]
  [NotifyCanExecuteChangedFor(nameof(AddPlacesCommand))]
  private DiffViewModel? _walkthrough;

  [ObservableProperty]
  [NotifyPropertyChangedFor(nameof(HasWalkthroughError))]
  private string _walkthroughError = string.Empty;

  public ScanViewModel(
    ConfigScanService scanService,
    ScanImportPlanner planner,
    Func<McpRegistry?> getRegistry,
    Action<int> applied)
  {
    _scanService = scanService;
    _planner = planner;
    _getRegistry = getRegistry;
    _applied = applied;
  }

  public ObservableCollection<ScannedFolderViewModel> Folders { get; } = [];

  public bool IsWalkthroughOpen => Walkthrough != null;

  public bool HasWalkthroughError => WalkthroughError.Length > 0;

  partial void OnWalkthroughChanged(DiffViewModel? oldValue, DiffViewModel? newValue)
  {
    if (oldValue != null)
    {
      oldValue.PropertyChanged -= OnWalkthroughPropertyChanged;
    }

    if (newValue != null)
    {
      newValue.PropertyChanged += OnWalkthroughPropertyChanged;
    }

    WalkthroughError = string.Empty;
  }

  [RelayCommand(IncludeCancelCommand = true)]
  private async Task ScanAsync(CancellationToken cancellationToken)
  {
    McpRegistry? registry = _getRegistry();
    if (registry == null)
    {
      return;
    }

    CancelWalkthrough();
    Folders.Clear();
    GlobalSettings settings = registry.Settings;
    List<string> roots = [.. RegistryService.GetDefaultScanRoots(settings)];
    RootsText = "Scan folders: " + string.Join(", ", roots);
    IsScanning = true;
    Message = "Scanning…";
    try
    {
      IReadOnlyList<ScannedFolder> found = await Task.Run(
        () => _scanService.ScanAsync(roots, settings, cancellationToken), cancellationToken);
      registry = _getRegistry() ?? registry;
      foreach (ScannedFolder folder in found)
      {
        List<string> skipped = [];
        IReadOnlyList<ScanServerMatch> matches = [];
        string? error = null;
        try
        {
          matches = _planner.Match([folder], registry, skipped);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
          error = ex.Message;
        }

        string prefix = folder.Path + ": ";
        Folders.Add(new ScannedFolderViewModel(
          folder,
          ScanImportPlanner.FindPlace(registry, folder),
          matches,
          [.. skipped.Select(s => s.StartsWith(prefix, StringComparison.Ordinal) ? s[prefix.Length..] : s)],
          roots,
          error));
      }

      Message = Folders.Count switch
      {
        0 => "No MCP configurations found.",
        1 => "Found 1 folder.",
        _ => $"Found {Folders.Count} folders.",
      };
    }
    catch (OperationCanceledException)
    {
      Message = "Scan cancelled.";
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      Message = $"Scan failed: {ex.Message}";
    }
    finally
    {
      IsScanning = false;
    }
  }

  private bool CanAddSelected() => !IsScanning && Walkthrough == null;

  [RelayCommand(CanExecute = nameof(CanAddSelected))]
  private void AddSelected()
  {
    McpRegistry? registry = _getRegistry();
    if (registry == null)
    {
      return;
    }

    List<ScannedFolder> selected = [.. Folders.Where(f => f.IsSelected && f.CanSelect).Select(f => f.Folder)];
    if (selected.Count == 0)
    {
      Message = "Select at least one folder.";
      return;
    }

    IReadOnlyList<ScanServerMatch> matches;
    try
    {
      matches = _planner.Match(selected, registry);
    }
    catch (Exception ex)
    {
      Message = $"Could not compare: {ex.Message}";
      return;
    }

    List<ScanServerMatch> differing = [.. matches.Where(m => m.Kind == ScanMatchKind.Differs)];
    if (differing.Count == 0)
    {
      TryApply(registry, selected, matches, new Dictionary<ScanServerMatch, ScanChoice>());
      return;
    }

    _pendingFolders = selected;
    _pendingMatches = matches;
    _differing = differing;
    Walkthrough = new DiffViewModel(
      "Library",
      "Found in project",
      [.. differing.Select(m => m.Diff! with { ServerName = WalkthroughTitle(m, selected) })],
      WalkthroughChoices,
      (index, choice) => choice != DiffChoice.UseFoundVersion || differing[index].CanUseFoundVersion);
  }

  private bool CanAddPlaces() => Walkthrough?.IsComplete == true;

  [RelayCommand(CanExecute = nameof(CanAddPlaces))]
  private void AddPlaces()
  {
    McpRegistry? registry = _getRegistry();
    if (registry == null || Walkthrough is not { IsComplete: true } walkthrough)
    {
      return;
    }

    Dictionary<ScanServerMatch, ScanChoice> choices = [];
    for (int i = 0; i < _differing.Count; i++)
    {
      choices[_differing[i]] = ToScanChoice(walkthrough.Choices[i]!.Value);
    }

    if (TryApply(registry, _pendingFolders, _pendingMatches, choices))
    {
      CancelWalkthrough();
    }
  }

  [RelayCommand]
  public void CancelWalkthrough()
  {
    Walkthrough = null;
    _pendingFolders = [];
    _pendingMatches = [];
    _differing = [];
  }

  private bool TryApply(
    McpRegistry registry,
    List<ScannedFolder> folders,
    IReadOnlyList<ScanServerMatch> matches,
    IReadOnlyDictionary<ScanServerMatch, ScanChoice> choices)
  {
    try
    {
      _planner.Apply(registry, folders, matches, choices);
    }
    catch (ArgumentException ex)
    {
      WalkthroughError = ex.Message;
      Message = ex.Message;
      return false;
    }

    Message = string.Empty;
    _applied(folders.Count);
    return true;
  }

  private void OnWalkthroughPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
  {
    if (e.PropertyName == nameof(DiffViewModel.IsComplete))
    {
      AddPlacesCommand.NotifyCanExecuteChanged();
    }
  }

  private static string WalkthroughTitle(ScanServerMatch match, IEnumerable<ScannedFolder> folders)
  {
    ScannedFolder folder = folders.First(f => string.Equals(f.Path, match.FolderPath, StringComparison.Ordinal));
    string name = folder.IsGlobal ? "Global" : Path.GetFileName(folder.Path.TrimEnd('/', '\\'));
    return $"{match.Found.Name} · {name}/{ScannedFolderViewModel.RelativeFile(folder, match.Found.FilePath)}";
  }

  private static ScanChoice ToScanChoice(DiffChoice choice) => choice switch
  {
    DiffChoice.KeepLibrary => ScanChoice.KeepLibrary,
    DiffChoice.UseFoundVersion => ScanChoice.UseFoundVersion,
    DiffChoice.ImportSeparately => ScanChoice.ImportSeparately,
    _ => throw new ArgumentOutOfRangeException(nameof(choice), choice, null),
  };
}
