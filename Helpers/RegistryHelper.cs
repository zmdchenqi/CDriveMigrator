using Microsoft.Win32;

namespace CDriveMigrator.Helpers;

/// <summary>
/// Shared registry utilities used by ReferenceAnalyzer, MigrationEngine, and MainViewModel.
/// </summary>
public static class RegistryHelper
{
    /// <summary>
    /// Parses a full registry path (e.g. "HKEY_LOCAL_MACHINE\SOFTWARE\...")
    /// into a root RegistryKey and a sub-key string.
    /// Supports both long forms (HKEY_LOCAL_MACHINE) and short forms (HKLM, HKCU).
    /// </summary>
    public static (RegistryKey root, string subKey)? SplitRegistryPath(string fullPath)
    {
        if (fullPath.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.LocalMachine, fullPath["HKEY_LOCAL_MACHINE\\".Length..]);
        if (fullPath.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.LocalMachine, fullPath["HKLM\\".Length..]);
        if (fullPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.CurrentUser, fullPath["HKEY_CURRENT_USER\\".Length..]);
        if (fullPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.CurrentUser, fullPath["HKCU\\".Length..]);
        if (fullPath.StartsWith("HKEY_CLASSES_ROOT\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.ClassesRoot, fullPath["HKEY_CLASSES_ROOT\\".Length..]);

        return null;
    }

    /// <summary>
    /// Resolves a registry root hive name string to its RegistryKey.
    /// Returns null for unrecognized names.
    /// </summary>
    public static RegistryKey? GetRootKey(string hiveName) => hiveName switch
    {
        "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
        "HKEY_CURRENT_USER" => Registry.CurrentUser,
        "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
        _ => null
    };
}
