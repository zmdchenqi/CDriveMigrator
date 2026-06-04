using System.Diagnostics;

namespace CDriveMigrator.Helpers;

/// <summary>
/// Shared process utilities used by MigrationEngine and MainViewModel.
/// </summary>
public static class ProcessHelper
{
    /// <summary>
    /// Runs an external process synchronously with captured stdout.
    /// Reads stderr asynchronously to prevent deadlock when both streams are redirected.
    /// Returns the process exit code.
    /// </summary>
    public static int RunProcess(string fileName, string arguments, out string output, int timeoutMs = 30000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"无法启动进程: {fileName}");
        var stderrTask = proc.StandardError.ReadToEndAsync();
        output = proc.StandardOutput.ReadToEnd();
        stderrTask.Wait(timeoutMs);
        if (!proc.WaitForExit(timeoutMs))
            throw new TimeoutException($"进程 '{fileName}' 在 {timeoutMs / 1000} 秒内未退出");
        return proc.ExitCode;
    }

    /// <summary>
    /// Kills all running processes whose MainModule.FileName is under <paramref name="directoryPath"/>.
    /// Returns the list of file names of killed processes.
    /// </summary>
    public static async Task<List<string>> KillProcessesInDirectoryAsync(
        string directoryPath, Action<string>? log = null, CancellationToken ct = default)
    {
        var killed = new List<string>();

        foreach (var proc in Process.GetProcesses())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var exePath = proc.MainModule?.FileName;
                if (exePath != null && exePath.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase))
                {
                    log?.Invoke($"停止进程: {proc.ProcessName} (PID: {proc.Id})");
                    killed.Add(exePath);
                    proc.Kill(entireProcessTree: true);
                    await proc.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log?.Invoke($"⚠ 检查/终止进程失败: {ex.Message}");
            }
            finally
            {
                proc.Dispose();
            }
        }

        return killed;
    }
}
