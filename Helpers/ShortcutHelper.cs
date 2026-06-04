using System.IO;

namespace CDriveMigrator.Helpers;

/// <summary>
/// Shared shortcut (.lnk) utilities used by ReferenceAnalyzer and MainViewModel.
/// </summary>
public static class ShortcutHelper
{
    /// <summary>
    /// Reads the target path of a .lnk shortcut via COM interop.
    /// Falls back to WorkingDirectory when TargetPath is empty.
    /// </summary>
    public static string? GetShortcutTarget(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            string target = shortcut.TargetPath;
            string workDir = shortcut.WorkingDirectory;
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            return !string.IsNullOrEmpty(target) ? target : workDir;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the standard directories where Windows shortcuts are stored:
    /// Common Desktop, User Desktop, Common Start Menu, User Start Menu, Quick Launch.
    /// Only returns directories that exist.
    /// </summary>
    public static List<string> GetShortcutSearchDirectories()
    {
        var dirs = new List<string>();

        AddIfNotEmpty(dirs, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
        AddIfNotEmpty(dirs, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        AddIfNotEmpty(dirs, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
        AddIfNotEmpty(dirs, Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));

        var quickLaunch = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch");
        if (Directory.Exists(quickLaunch)) dirs.Add(quickLaunch);

        return dirs;
    }

    private static void AddIfNotEmpty(List<string> dirs, string path)
    {
        if (!string.IsNullOrEmpty(path)) dirs.Add(path);
    }
}
