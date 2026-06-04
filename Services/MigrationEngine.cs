using CDriveMigrator.Helpers;
using CDriveMigrator.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CDriveMigrator.Services;

public class MigrationEngine
{
    private readonly ReferenceAnalyzer _analyzer = new();
    private static readonly string SnapshotDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CDriveMigrator", "snapshots");

    public event Action<string>? Log;
    public event Action<double>? ProgressChanged;

    public async Task<MigrationResult> MigrateAsync(InstalledProgram program, string targetRoot,
        CancellationToken ct = default)
    {
        var result = new MigrationResult { ProgramName = program.Name };
        MigrationSnapshot? snapshot = null;

        var sourcePath = program.InstallLocation.TrimEnd('\\');
        var folderName = Path.GetFileName(sourcePath);
        var targetPath = Path.Combine(targetRoot, folderName);

        if (string.IsNullOrEmpty(folderName))
            throw new InvalidOperationException($"无法从路径提取文件夹名: {sourcePath}");

        try
        {
            // Step 1: Validate
            program.Status = MigrationStatus.Analyzing;
            Report(0, "验证迁移条件...");

            if (!Directory.Exists(sourcePath))
                throw new InvalidOperationException($"源目录不存在: {sourcePath}");

            if (JunctionManager.IsJunction(sourcePath))
                throw new InvalidOperationException("源目录已经是联接点，可能已经迁移过");

            if (Directory.Exists(targetPath))
                throw new InvalidOperationException($"目标目录已存在: {targetPath}");

            var targetDrive = Path.GetPathRoot(targetPath);
            if (targetDrive != null)
            {
                var driveInfo = new DriveInfo(targetDrive);
                if (driveInfo.AvailableFreeSpace < program.SizeBytes * 1.1)
                    throw new InvalidOperationException(
                        $"目标磁盘空间不足: 需要 {InstalledProgram.FormatSize(program.SizeBytes)}，" +
                        $"可用 {InstalledProgram.FormatSize(driveInfo.AvailableFreeSpace)}");
            }

            // Step 2: Ensure analyzed
            if (!program.IsAnalyzed)
            {
                Report(5, "分析程序引用...");
                await _analyzer.AnalyzeAsync(program, new Progress<string>(s => Log?.Invoke(s)), ct);
            }

            // Step 3: Create snapshot for rollback
            Report(10, "创建回滚快照...");
            snapshot = CreateSnapshot(program, sourcePath, targetPath);
            SaveSnapshot(snapshot);

            // Step 4: Kill related processes
            Report(15, "停止相关进程...");
            program.Status = MigrationStatus.Moving;
            await KillRelatedProcesses(program, sourcePath, snapshot, ct);

            // Step 5: Stop related services
            Report(18, "停止相关服务...");
            await StopRelatedServices(program, ct, result);

            // Step 6: Move files
            Report(20, $"迁移文件: {sourcePath} → {targetPath}");
            await MoveFilesAsync(sourcePath, targetPath, ct);
            snapshot.FilesMoved = true;
            SaveSnapshot(snapshot);
            Report(60, "文件迁移完成");

            // Step 7: Fix all references
            program.Status = MigrationStatus.FixingReferences;
            Report(62, "修复注册表引用...");
            FixRegistryReferences(program, sourcePath, targetPath, result);

            Report(75, "修复快捷方式...");
            FixShortcutReferences(program, sourcePath, targetPath, result);

            Report(80, "修复服务路径...");
            FixServiceReferences(program, sourcePath, targetPath, result);

            Report(83, "修复环境变量...");
            FixEnvironmentReferences(program, sourcePath, targetPath, result);

            Report(86, "修复计划任务...");
            FixScheduledTaskReferences(program, sourcePath, targetPath, result);

            Report(89, "修复内部配置文件...");
            FixConfigFileReferences(program, sourcePath, targetPath, result);

            // Step 8: Create junction as safety net
            program.Status = MigrationStatus.CreatingJunction;
            Report(92, $"创建目录联接: {sourcePath} → {targetPath}");
            JunctionManager.CreateJunction(sourcePath, targetPath);
            snapshot.JunctionCreated = true;
            SaveSnapshot(snapshot);

            // Step 9: Restart services
            Report(95, "重新启动服务...");
            await RestartServices(program, ct, result);

            // Step 10: Verify
            program.Status = MigrationStatus.Verifying;
            Report(97, "验证迁移结果...");
            VerifyMigration(program, targetPath, result);

            // Done
            Report(100, $"✓ {program.Name} 迁移完成！释放空间: {InstalledProgram.FormatSize(program.SizeBytes)}");
            program.Status = MigrationStatus.Completed;
            program.InstallLocation = targetPath; // 更新安装路径为新位置
            result.Success = true;
            result.SpaceSaved = program.SizeBytes;
            result.Message = $"迁移成功，共修复 {result.FixedReferences.Count} 个引用";

            if (result.FailedFixes.Count > 0)
                result.Message += $"，{result.FailedFixes.Count} 个修复失败（已通过 Junction 兜底）";
        }
        catch (OperationCanceledException)
        {
            Log?.Invoke("迁移已取消，正在回滚...");
            if (snapshot != null)
                await RollbackAsync(snapshot);
            program.Status = MigrationStatus.RolledBack;
            result.Success = false;
            result.Message = "用户取消，已回滚";
        }
        catch (Exception ex)
        {
            Log?.Invoke($"✗ 迁移失败: {ex.Message}");
            if (snapshot != null)
            {
                Log?.Invoke("正在回滚...");
                var rollbackOk = await RollbackAsync(snapshot);
                if (!rollbackOk)
                {
                    result.Warnings.Add("回滚过程中出现错误，系统可能处于不一致状态，请手动检查");
                    Log?.Invoke("⚠ 回滚未完全成功，请手动检查系统状态");
                }
            }
            program.Status = MigrationStatus.Failed;
            result.Success = false;
            result.Message = $"迁移失败: {ex.Message}";
        }

        return result;
    }

    #region File Operations

    private async Task MoveFilesAsync(string source, string target, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        // Use robocopy for reliable cross-volume move with retry
        var psi = new ProcessStartInfo
        {
            FileName = "robocopy",
            Arguments = $"\"{source}\" \"{target}\" /E /MOVE /R:5 /W:2 /NP /NDL /NJH /NJS",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        Log?.Invoke($"  robocopy /E /MOVE /R:5 /W:2");
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 robocopy 进程");

        var outputTask = Task.Run(async () =>
        {
            while (!proc.StandardOutput.EndOfStream)
            {
                var line = await proc.StandardOutput.ReadLineAsync(ct);
                if (!string.IsNullOrWhiteSpace(line))
                    Log?.Invoke($"  {line.Trim()}");
            }
        }, ct);

        await proc.WaitForExitAsync(ct);
        await outputTask;

        // Robocopy exit codes: 0-7 are success, 8+ are errors
        if (proc.ExitCode >= 8)
        {
            Log?.Invoke($"  robocopy 返回代码 {proc.ExitCode}，尝试处理被占用文件...");
            // 处理被占用的残留文件 — 找到那些进程并强制终止后重试
            await ForceKillLockingProcesses(source, ct);
            
            // 二次尝试：用robocopy处理残留文件
            var retryPsi = new ProcessStartInfo
            {
                FileName = "robocopy",
                Arguments = $"\"{source}\" \"{target}\" /E /MOVE /R:2 /W:1 /NP /NDL /NFL /NJH /NJS",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var retryProc = Process.Start(retryPsi);
            if (retryProc == null)
            {
                Log?.Invoke("  ⚠ 无法启动 robocopy 重试进程");
                return;
            }
            await retryProc.WaitForExitAsync(ct);

            if (retryProc.ExitCode >= 8 && Directory.Exists(source) && 
                Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Any())
            {
                // 还有文件锁死 — 标记重启后清理
                Log?.Invoke("  ⚠ 部分文件被锁定，已标记重启后移动");
                ScheduleLockedFileMove(source, target);
            }
        }

        // Clean up empty source directory if still exists
        if (Directory.Exists(source))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(source).Any())
                    Directory.Delete(source, true);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"  ⚠ 清理空源目录失败: {ex.Message}");
            }
        }
    }

    private async Task ForceKillLockingProcesses(string dirPath, CancellationToken ct)
    {
        await ProcessHelper.KillProcessesInDirectoryAsync(
            dirPath, msg => Log?.Invoke($"  强制{msg}"), ct);

        // 等待文件句柄释放
        await Task.Delay(2000, ct);
    }

    private void ScheduleLockedFileMove(string source, string target)
    {
        // 使用 MoveFileEx 标记重启后移动（MOVEFILE_DELAY_UNTIL_REBOOT）
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(source, file);
                var destFile = Path.Combine(target, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile)!);
                
                // 先复制到目标（如果可以读取）
                try
                {
                    if (!File.Exists(destFile))
                        File.Copy(file, destFile, true);
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"  ⚠ 复制锁定文件失败 {Path.GetFileName(file)}: {ex.Message}");
                }

                // 标记重启后删除源文件
                NativeMethods.MoveFileEx(file, null, 
                    NativeMethods.MoveFileFlags.MOVEFILE_DELAY_UNTIL_REBOOT);
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"  ⚠ 标记延迟删除失败: {ex.Message}");
        }
    }

    #endregion

    #region Reference Fixing

    private void FixRegistryReferences(InstalledProgram program, string oldPath, string newPath, MigrationResult result)
    {
        var regRefs = program.References.Where(r => r.Type == ReferenceType.RegistryValue).ToList();
        foreach (var r in regRefs)
        {
            try
            {
                var parts = RegistryHelper.SplitRegistryPath(r.Location);
                if (parts == null) { result.FailedFixes.Add($"无法解析: {r.Location}"); continue; }

                using var key = parts.Value.root.OpenSubKey(parts.Value.subKey, writable: true);
                if (key == null) { result.FailedFixes.Add($"无法打开: {r.Location}"); continue; }

                var kind = key.GetValueKind(r.ValueName);
                var currentValue = key.GetValue(r.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if (currentValue == null) continue;

                var newValue = PathHelper.ReplacePathInString(currentValue, oldPath, newPath);
                if (newValue != currentValue)
                {
                    key.SetValue(r.ValueName, newValue, kind);
                    r.NewValue = newValue;
                    r.IsFixed = true;
                    result.FixedReferences.Add($"注册表: {r.Location}\\{r.ValueName}");
                    Log?.Invoke($"  ✓ 注册表: {r.ValueName}");
                }
            }
            catch (Exception ex)
            {
                result.FailedFixes.Add($"注册表修复失败 {r.Location}\\{r.ValueName}: {ex.Message}");
                Log?.Invoke($"  ✗ 注册表: {r.ValueName} - {ex.Message}");
            }
        }
    }

    private void FixShortcutReferences(InstalledProgram program, string oldPath, string newPath, MigrationResult result)
    {
        var refs = program.References.Where(r => r.Type == ReferenceType.Shortcut).ToList();
        foreach (var r in refs)
        {
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) continue;
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(r.Location);

                string target = shortcut.TargetPath ?? "";
                string workDir = shortcut.WorkingDirectory ?? "";
                string iconLoc = shortcut.IconLocation ?? "";
                bool changed = false;

                var newTarget = PathHelper.ReplacePathInString(target, oldPath, newPath);
                if (newTarget != target) { shortcut.TargetPath = newTarget; changed = true; }

                var newWorkDir = PathHelper.ReplacePathInString(workDir, oldPath, newPath);
                if (newWorkDir != workDir) { shortcut.WorkingDirectory = newWorkDir; changed = true; }

                var newIcon = PathHelper.ReplacePathInString(iconLoc, oldPath, newPath);
                if (newIcon != iconLoc) { shortcut.IconLocation = newIcon; changed = true; }

                if (changed)
                {
                    shortcut.Save();
                    r.NewValue = newTarget;
                    r.IsFixed = true;
                    result.FixedReferences.Add($"快捷方式: {Path.GetFileName(r.Location)}");
                    Log?.Invoke($"  ✓ 快捷方式: {Path.GetFileName(r.Location)}");
                }

                System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
                System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
            catch (Exception ex)
            {
                result.FailedFixes.Add($"快捷方式修复失败 {r.Location}: {ex.Message}");
                Log?.Invoke($"  ✗ 快捷方式: {Path.GetFileName(r.Location)} - {ex.Message}");
            }
        }
    }

    private void FixServiceReferences(InstalledProgram program, string oldPath, string newPath, MigrationResult result)
    {
        var refs = program.References.Where(r => r.Type == ReferenceType.WindowsService).ToList();
        foreach (var r in refs)
        {
            try
            {
                var svcName = r.ValueName;
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{svcName}", writable: true);
                if (key == null) continue;

                var imagePath = key.GetValue("ImagePath") as string;
                if (imagePath == null) continue;

                var newImagePath = PathHelper.ReplacePathInString(imagePath, oldPath, newPath);
                if (newImagePath != imagePath)
                {
                    key.SetValue("ImagePath", newImagePath);
                    r.NewValue = newImagePath;
                    r.IsFixed = true;
                    result.FixedReferences.Add($"服务: {svcName}");
                    Log?.Invoke($"  ✓ 服务: {r.Description}");
                }
            }
            catch (Exception ex)
            {
                result.FailedFixes.Add($"服务修复失败 {r.ValueName}: {ex.Message}");
                Log?.Invoke($"  ✗ 服务: {r.ValueName} - {ex.Message}");
            }
        }
    }

    private void FixEnvironmentReferences(InstalledProgram program, string oldPath, string newPath, MigrationResult result)
    {
        var refs = program.References.Where(r => r.Type == ReferenceType.EnvironmentVariable).ToList();
        foreach (var r in refs)
        {
            try
            {
                var isSystem = r.Location.Contains("HKLM", StringComparison.OrdinalIgnoreCase);
                var regPath = isSystem
                    ? @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"
                    : @"Environment";
                var rootKey = isSystem ? Registry.LocalMachine : Registry.CurrentUser;

                using var key = rootKey.OpenSubKey(regPath, writable: true);
                if (key == null) continue;

                var kind = key.GetValueKind(r.ValueName);
                var currentValue = key.GetValue(r.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                if (currentValue == null) continue;

                var newValue = PathHelper.ReplacePathInString(currentValue, oldPath, newPath);
                if (newValue != currentValue)
                {
                    key.SetValue(r.ValueName, newValue, kind);
                    r.NewValue = newValue;
                    r.IsFixed = true;
                    result.FixedReferences.Add($"环境变量: {r.ValueName}");
                    Log?.Invoke($"  ✓ 环境变量: {r.Description}");

                    // Broadcast WM_SETTINGCHANGE
                    BroadcastEnvironmentChange();
                }
            }
            catch (Exception ex)
            {
                result.FailedFixes.Add($"环境变量修复失败 {r.ValueName}: {ex.Message}");
                Log?.Invoke($"  ✗ 环境变量: {r.ValueName} - {ex.Message}");
            }
        }
    }

    private void FixScheduledTaskReferences(InstalledProgram program, string oldPath, string newPath, MigrationResult result)
    {
        var refs = program.References.Where(r => r.Type == ReferenceType.ScheduledTask).ToList();
        foreach (var r in refs)
        {
            try
            {
                var taskName = r.ValueName;
                // Export task XML, modify paths, re-import
                var exportPath = Path.GetTempFileName();
                try
                {
                    // Export
                    ProcessHelper.RunProcess("schtasks.exe", $"/query /tn \"{taskName}\" /xml ONE", out var xml);
                    if (string.IsNullOrEmpty(xml)) continue;

                    var newXml = PathHelper.ReplacePathInString(xml, oldPath, newPath);
                    if (newXml == xml) continue;

                    File.WriteAllText(exportPath, newXml, Encoding.Unicode);

                    // Delete and recreate
                    ProcessHelper.RunProcess("schtasks.exe", $"/delete /tn \"{taskName}\" /f", out _);
                    ProcessHelper.RunProcess("schtasks.exe", $"/create /tn \"{taskName}\" /xml \"{exportPath}\"", out var createOutput);

                    r.NewValue = newPath;
                    r.IsFixed = true;
                    result.FixedReferences.Add($"计划任务: {taskName}");
                    Log?.Invoke($"  ✓ 计划任务: {taskName}");
                }
                finally
                {
                    try { File.Delete(exportPath); } catch { }
                }
            }
            catch (Exception ex)
            {
                result.FailedFixes.Add($"计划任务修复失败 {r.ValueName}: {ex.Message}");
                result.Warnings.Add($"计划任务 {r.ValueName} 修复失败，但 Junction 链接将保证其继续运行");
                Log?.Invoke($"  ⚠ 计划任务: {r.ValueName} - {ex.Message} (Junction 兜底)");
            }
        }
    }

    private void FixConfigFileReferences(InstalledProgram program, string oldPath, string newPath, MigrationResult result)
    {
        var refs = program.References.Where(r => r.Type == ReferenceType.ConfigFile).ToList();
        foreach (var r in refs)
        {
            try
            {
                // Config files have been moved, so update path in new location
                var newFilePath = r.Location.Replace(oldPath, newPath, StringComparison.OrdinalIgnoreCase);
                if (!File.Exists(newFilePath)) continue;

                var content = File.ReadAllText(newFilePath);
                var newContent = PathHelper.ReplacePathInString(content, oldPath, newPath);

                if (newContent != content)
                {
                    File.WriteAllText(newFilePath, newContent);
                    r.NewValue = newPath;
                    r.IsFixed = true;
                    result.FixedReferences.Add($"配置文件: {Path.GetFileName(r.Location)}");
                    Log?.Invoke($"  ✓ 配置文件: {Path.GetFileName(r.Location)}");
                }
            }
            catch (Exception ex)
            {
                result.FailedFixes.Add($"配置文件修复失败 {r.Location}: {ex.Message}");
                Log?.Invoke($"  ✗ 配置文件: {Path.GetFileName(r.Location)} - {ex.Message}");
            }
        }
    }

    #endregion

    #region Process / Service Management

    private async Task KillRelatedProcesses(InstalledProgram program, string installPath,
        MigrationSnapshot snapshot, CancellationToken ct)
    {
        var killed = await ProcessHelper.KillProcessesInDirectoryAsync(
            installPath, msg => Log?.Invoke($"  {msg}"), ct);
        snapshot.KilledProcesses.AddRange(killed);
    }

    private async Task StopRelatedServices(InstalledProgram program, CancellationToken ct, MigrationResult? result = null)
    {
        var svcRefs = program.References.Where(r => r.Type == ReferenceType.WindowsService).ToList();
        foreach (var r in svcRefs)
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController(r.ValueName);
                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                {
                    Log?.Invoke($"  停止服务: {r.Description}");
                    sc.Stop();
                    sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"  ⚠ 无法停止服务 {r.ValueName}: {ex.Message}");
                result?.Warnings.Add($"无法停止服务 {r.ValueName}: {ex.Message}");
            }
        }
    }

    private async Task RestartServices(InstalledProgram program, CancellationToken ct, MigrationResult? result = null)
    {
        var svcRefs = program.References.Where(r => r.Type == ReferenceType.WindowsService && r.IsFixed).ToList();
        foreach (var r in svcRefs)
        {
            try
            {
                using var sc = new System.ServiceProcess.ServiceController(r.ValueName);
                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Stopped)
                {
                    Log?.Invoke($"  启动服务: {r.Description}");
                    sc.Start();
                    sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"  ⚠ 无法启动服务 {r.ValueName}: {ex.Message}");
                result?.Warnings.Add($"无法重启服务 {r.ValueName}: {ex.Message}");
            }
        }
    }

    #endregion

    #region Rollback

    public async Task<bool> RollbackAsync(MigrationSnapshot snapshot)
    {
        Log?.Invoke($"开始回滚: {snapshot.ProgramName}");
        var errors = new List<string>();

        try
        {
            // 1. Remove junction if created
            if (snapshot.JunctionCreated && JunctionManager.IsJunction(snapshot.SourcePath))
            {
                Log?.Invoke("  移除联接点...");
                try
                {
                    JunctionManager.RemoveJunction(snapshot.SourcePath);
                }
                catch (Exception ex)
                {
                    var msg = $"移除联接点失败: {ex.Message}";
                    Log?.Invoke($"  ⚠ {msg}");
                    errors.Add(msg);
                }
            }

            // 2. Restore registry values
            Log?.Invoke("  恢复注册表...");
            foreach (var backup in snapshot.ReferenceBackups.Where(b => b.WasModified))
            {
                try
                {
                    RestoreRegistryValue(backup);
                }
                catch (Exception ex)
                {
                    var msg = $"注册表恢复失败 {backup.Reference.Location}: {ex.Message}";
                    Log?.Invoke($"  ⚠ {msg}");
                    errors.Add(msg);
                }
            }

            // 3. Move files back
            if (snapshot.FilesMoved && Directory.Exists(snapshot.TargetPath))
            {
                Log?.Invoke("  移回文件...");
                if (Directory.Exists(snapshot.SourcePath) && !JunctionManager.IsJunction(snapshot.SourcePath))
                    Directory.Delete(snapshot.SourcePath, true);

                await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "robocopy",
                        Arguments = $"\"{snapshot.TargetPath}\" \"{snapshot.SourcePath}\" /E /MOVE /R:3 /W:1 /NP /NDL /NFL /NJH /NJS",
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc == null)
                    {
                        var msg = "无法启动 robocopy 进行文件回滚";
                        Log?.Invoke($"  ⚠ {msg}");
                        errors.Add(msg);
                        return;
                    }
                    proc.WaitForExit(300000);
                    if (proc.ExitCode >= 8)
                    {
                        var stderr = proc.StandardError.ReadToEnd();
                        var msg = $"文件回滚 robocopy 返回错误代码 {proc.ExitCode}: {stderr}";
                        Log?.Invoke($"  ⚠ {msg}");
                        errors.Add(msg);
                    }
                });
            }

            // 4. Broadcast env change
            BroadcastEnvironmentChange();

            if (errors.Count == 0)
            {
                Log?.Invoke($"✓ 回滚完成: {snapshot.ProgramName}");
                return true;
            }
            else
            {
                Log?.Invoke($"⚠ 回滚完成但有 {errors.Count} 个错误: {snapshot.ProgramName}");
                foreach (var err in errors)
                    Log?.Invoke($"  - {err}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Log?.Invoke($"✗ 回滚出错: {ex.Message}");
            return false;
        }
    }

    private static void RestoreRegistryValue(ReferenceBackup backup)
    {
        var r = backup.Reference;
        if (r.Type != ReferenceType.RegistryValue && r.Type != ReferenceType.EnvironmentVariable) return;

        var parts = RegistryHelper.SplitRegistryPath(r.Location);
        if (parts == null) return;

        using var key = parts.Value.root.OpenSubKey(parts.Value.subKey, writable: true);
        if (key == null) return;

        var kind = key.GetValueKind(r.ValueName);
        key.SetValue(r.ValueName, backup.BackupValue, kind);
    }

    #endregion

    #region Snapshot

    private MigrationSnapshot CreateSnapshot(InstalledProgram program, string sourcePath, string targetPath)
    {
        var snapshot = new MigrationSnapshot
        {
            ProgramName = program.Name,
            SourcePath = sourcePath,
            TargetPath = targetPath,
            Timestamp = DateTime.Now
        };

        foreach (var r in program.References)
        {
            snapshot.ReferenceBackups.Add(new ReferenceBackup
            {
                Reference = r,
                BackupValue = r.OriginalValue,
                WasModified = false
            });
        }

        return snapshot;
    }

    private void SaveSnapshot(MigrationSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(SnapshotDir);
            var fileName = $"{snapshot.ProgramName.Replace(" ", "_")}_{snapshot.Timestamp:yyyyMMdd_HHmmss}.json";
            var filePath = Path.Combine(SnapshotDir, SanitizeFileName(fileName));
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"⚠ 保存回滚快照失败: {ex.Message}");
            throw new InvalidOperationException($"无法保存回滚快照，迁移终止以保障安全: {ex.Message}", ex);
        }
    }

    private void VerifyMigration(InstalledProgram program, string targetPath, MigrationResult result)
    {
        if (!Directory.Exists(targetPath))
        {
            result.Warnings.Add("目标目录不存在");
            return;
        }

        // Check junction
        if (JunctionManager.IsJunction(program.InstallLocation))
        {
            Log?.Invoke("  ✓ Junction 联接点正常");
        }
        else
        {
            result.Warnings.Add("Junction 联接点未创建");
        }

        // Check if key executables are accessible through junction
        try
        {
            var exeFiles = Directory.GetFiles(program.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly);
            foreach (var exe in exeFiles.Take(3))
            {
                if (File.Exists(exe))
                    Log?.Invoke($"  ✓ 可访问: {Path.GetFileName(exe)}");
                else
                    result.Warnings.Add($"无法通过 Junction 访问: {Path.GetFileName(exe)}");
            }
        }
        catch (Exception ex)
        {
            var msg = $"验证可执行文件可访问性时出错: {ex.Message}";
            Log?.Invoke($"  ⚠ {msg}");
            result.Warnings.Add(msg);
        }
    }

    #endregion

    #region Helpers

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

    private void BroadcastEnvironmentChange()
    {
        try
        {
            const uint WM_SETTINGCHANGE = 0x001A;
            const uint SMTO_ABORTIFHUNG = 0x0002;
            var HWND_BROADCAST = new IntPtr(0xFFFF);
            SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero,
                "Environment", SMTO_ABORTIFHUNG, 5000, out _);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"⚠ 广播环境变量更新失败: {ex.Message}");
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private void Report(double progress, string message)
    {
        ProgressChanged?.Invoke(progress);
        Log?.Invoke(message);
    }

    #endregion
}
