namespace McpManager.Core.Services;

public static class ProjectFolderRules
{
  private static readonly string[] TestWords = ["tests", "test"];

  public static bool PathsEqual(string left, string right) =>
    string.Equals(
      Path.TrimEndingDirectorySeparator(left), Path.TrimEndingDirectorySeparator(right),
      StringComparison.OrdinalIgnoreCase);

  public static bool LooksLikeTest(string path, IEnumerable<string> roots)
  {
    string relative = roots
      .Select(root => Path.GetRelativePath(root, path))
      .Where(r => r != ".." && !r.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        && !Path.IsPathRooted(r))
      .MinBy(r => r.Length) ?? path;
    return relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(IsTestSegment);
  }

  private static bool IsTestSegment(string segment) => TestWords.Any(word =>
    (segment.StartsWith(word, StringComparison.OrdinalIgnoreCase) && IsBoundary(segment, word.Length))
    || (segment.EndsWith(word, StringComparison.OrdinalIgnoreCase) && IsBoundary(segment, segment.Length - word.Length)));

  private static bool IsBoundary(string segment, int index) =>
    index <= 0 || index >= segment.Length || !char.IsLetter(segment[index]) || !char.IsLetter(segment[index - 1])
    || (char.IsLower(segment[index - 1]) && char.IsUpper(segment[index]));
}
