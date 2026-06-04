using System.Text;

namespace CDriveMigrator.Helpers;

/// <summary>
/// Shared path-matching and replacement utilities used by ReferenceAnalyzer and MigrationEngine.
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// Checks whether <paramref name="text"/> contains <paramref name="path"/>
    /// using case-insensitive matching, including backslash/forward-slash and quoted variants.
    /// </summary>
    public static bool ContainsPath(string text, string path)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(path)) return false;

        if (text.Contains(path, StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains(path + "\\", StringComparison.OrdinalIgnoreCase)) return true;

        var fwdPath = path.Replace('\\', '/');
        if (text.Contains(fwdPath, StringComparison.OrdinalIgnoreCase)) return true;

        var quotedPath = "\"" + path;
        if (text.Contains(quotedPath, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    /// <summary>
    /// Replaces all occurrences of <paramref name="oldPath"/> in <paramref name="text"/>
    /// with <paramref name="newPath"/>, handling backslash/forward-slash and quoted variants.
    /// </summary>
    public static string ReplacePathInString(string text, string oldPath, string newPath)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var result = text;

        result = ReplaceIgnoreCase(result, oldPath + "\\", newPath + "\\");
        result = ReplaceIgnoreCase(result, oldPath + "/", newPath + "/");
        result = ReplaceIgnoreCase(result, oldPath + "\"", newPath + "\"");
        result = ReplaceIgnoreCase(result, oldPath, newPath);

        var oldFwd = oldPath.Replace('\\', '/');
        var newFwd = newPath.Replace('\\', '/');
        result = ReplaceIgnoreCase(result, oldFwd, newFwd);

        return result;
    }

    /// <summary>
    /// Case-insensitive string replacement.
    /// </summary>
    public static string ReplaceIgnoreCase(string input, string oldValue, string newValue)
    {
        int index = 0;
        var sb = new StringBuilder(input.Length);
        while (index < input.Length)
        {
            var pos = input.IndexOf(oldValue, index, StringComparison.OrdinalIgnoreCase);
            if (pos < 0)
            {
                sb.Append(input, index, input.Length - index);
                break;
            }
            sb.Append(input, index, pos - index);
            sb.Append(newValue);
            index = pos + oldValue.Length;
        }
        return sb.ToString();
    }
}
