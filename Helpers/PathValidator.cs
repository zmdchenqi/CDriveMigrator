using System.IO;
using System.Text.RegularExpressions;

namespace CDriveMigrator.Helpers;

/// <summary>
/// Validates and sanitizes file system paths to prevent command injection
/// and path traversal attacks when paths are passed to shell commands.
/// </summary>
internal static partial class PathValidator
{
    // Characters that can break out of quoted strings in cmd.exe
    private static readonly char[] DangerousShellChars =
        ['&', '|', ';', '`', '$', '(', ')', '{', '}', '<', '>', '!', '^', '\n', '\r'];

    /// <summary>
    /// Validates that a path is safe to use in shell commands (cmd.exe arguments).
    /// Returns true if the path is safe; false if it contains characters that could
    /// enable command injection.
    /// </summary>
    public static bool IsSafeForShell(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Reject paths containing shell metacharacters
        foreach (var c in DangerousShellChars)
        {
            if (path.Contains(c))
                return false;
        }

        // Reject paths with unbalanced or embedded quotes
        if (path.Contains('"'))
            return false;

        return true;
    }

    /// <summary>
    /// Validates that a path is a well-formed, absolute Windows path without
    /// traversal sequences or dangerous characters.
    /// </summary>
    public static bool IsValidAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Must be rooted (e.g. C:\...)
        if (!Path.IsPathRooted(path))
            return false;

        // Reject traversal sequences
        if (path.Contains(".."))
            return false;

        // Must be safe for shell use
        if (!IsSafeForShell(path))
            return false;

        return true;
    }

    /// <summary>
    /// Validates a user-provided subfolder name for path safety.
    /// Rejects empty, traversal, and shell-unsafe values.
    /// </summary>
    public static bool IsValidSubFolder(string subFolder)
    {
        if (string.IsNullOrWhiteSpace(subFolder))
            return false;

        // Reject traversal
        if (subFolder.Contains(".."))
            return false;

        // Reject invalid filename characters (except path separator)
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar)
                continue;
            if (subFolder.Contains(c))
                return false;
        }

        // Reject shell-dangerous characters
        if (!IsSafeForShell(subFolder))
            return false;

        return true;
    }

    /// <summary>
    /// Validates a scheduled task name for safe use in schtasks.exe arguments.
    /// </summary>
    public static bool IsValidTaskName(string taskName)
    {
        if (string.IsNullOrWhiteSpace(taskName))
            return false;

        // Task names should not contain shell metacharacters or quotes
        if (!IsSafeForShell(taskName))
            return false;

        if (taskName.Contains('"'))
            return false;

        return true;
    }

    /// <summary>
    /// Ensures both source and target paths are valid for a migration operation.
    /// Throws InvalidOperationException with a user-friendly message if validation fails.
    /// </summary>
    public static void ValidateMigrationPaths(string sourcePath, string targetPath)
    {
        if (!IsValidAbsolutePath(sourcePath))
            throw new InvalidOperationException(
                $"源路径包含不安全字符或格式无效: {SanitizeForDisplay(sourcePath)}");

        if (!IsValidAbsolutePath(targetPath))
            throw new InvalidOperationException(
                $"目标路径包含不安全字符或格式无效: {SanitizeForDisplay(targetPath)}");
    }

    /// <summary>
    /// Removes potentially dangerous characters from a string for safe display in logs/UI.
    /// Does NOT make the string safe for shell execution — use IsSafeForShell for that.
    /// </summary>
    public static string SanitizeForDisplay(string input)
    {
        if (string.IsNullOrEmpty(input))
            return "";

        // Replace control characters and shell metacharacters with underscores for display
        var sanitized = new char[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (char.IsControl(c) || Array.IndexOf(DangerousShellChars, c) >= 0)
                sanitized[i] = '_';
            else
                sanitized[i] = c;
        }
        return new string(sanitized);
    }
}
