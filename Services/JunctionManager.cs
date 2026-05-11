using System.Diagnostics;
using System.IO;

namespace CDriveMigrator.Services;

public static class JunctionManager
{
    public static void CreateJunction(string junctionPath, string targetPath)
    {
        if (Directory.Exists(junctionPath))
        {
            if (IsJunction(junctionPath))
                Directory.Delete(junctionPath, false);
            else
                throw new InvalidOperationException($"目录已存在且不是联接点: {junctionPath}");
        }

        targetPath = Path.GetFullPath(targetPath);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c mklink /J \"{junctionPath}\" \"{targetPath}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)!;
        proc.WaitForExit(30000);

        if (proc.ExitCode != 0)
        {
            var err = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException($"创建联接点失败: {err}");
        }
    }

    public static void RemoveJunction(string junctionPath)
    {
        if (IsJunction(junctionPath))
        {
            Directory.Delete(junctionPath, false);
        }
    }

    public static bool IsJunction(string path)
    {
        try
        {
            var di = new DirectoryInfo(path);
            return di.Exists && di.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }

    public static string? GetJunctionTarget(string junctionPath)
    {
        if (!IsJunction(junctionPath)) return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c dir \"{Path.GetDirectoryName(junctionPath)}\" /AL",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(10000);

            var dirName = Path.GetFileName(junctionPath);
            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains(dirName) && line.Contains("["))
                {
                    var start = line.IndexOf('[') + 1;
                    var end = line.IndexOf(']');
                    if (start > 0 && end > start)
                        return line[start..end];
                }
            }
        }
        catch { }
        return null;
    }
}
