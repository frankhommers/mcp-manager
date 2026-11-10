using System.Text.Json;
using McpManager.Core.Models;

namespace McpManager.Core.Services;

public class UiStateStore(string? filePath)
{
  public const string FileName = "ui-state.json";

  private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

  public static UiStateStore NextTo(string registryFilePath)
  {
    string? directory = string.IsNullOrWhiteSpace(registryFilePath) ? null : Path.GetDirectoryName(registryFilePath);
    return new UiStateStore(string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, FileName));
  }

  public UiState Load()
  {
    if (filePath == null || !File.Exists(filePath))
    {
      return new UiState();
    }

    try
    {
      return JsonSerializer.Deserialize<UiState>(File.ReadAllText(filePath), JsonOptions) ?? new UiState();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
      return new UiState();
    }
  }

  public void Save(UiState state)
  {
    if (filePath == null)
    {
      return;
    }

    try
    {
      Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
      File.WriteAllText(filePath, JsonSerializer.Serialize(state, JsonOptions));
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
    }
  }
}
