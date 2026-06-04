using CDriveMigrator.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.ServiceProcess;

namespace CDriveMigrator.Services;

public class ReferenceAnalyzer
{
    // 计划任务缓存 — 只执行一次 schtasks
    private List<string>? _scheduledTasksCache;
    private readonly object _taskCacheLock = new();

    private static readonly string[] ConfigExtensions =
        [".ini", ".cfg", ".conf", ".config", ".xml", ".json", ".yaml", ".yml", ".properties", ".toml", ".env"];

    private static readonly string[] RegistrySearchRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
    ];

    private static readonly string[] RegistryDeepScanSkip =
    [
        @"Microsoft\Windows\CurrentVersion\Installer",
        @"Microsoft\Windows\CurrentVersion\SideBySide",
        @"Microsoft\Windows\CurrentVersion\Component Based Servicing",
        @"Classes\Installer",
        @"Microsoft\Cryptography",
        @"Microsoft\Windows\CurrentVersion\Uninstall",
        @"WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        @"Microsoft\Windows NT",
        @"Microsoft\Windows\CurrentVersion\Explorer\ComDlg32",
        @"Microsoft\Windows\CurrentVersion\Explorer\UserAssist",
        @"Microsoft\Windows\CurrentVersion\Internet Settings",
        @"Microsoft\Windows\CurrentVersion\Shell",
        @"Microsoft\SystemCertificates",
        @"Policies",
        @"Classes\CLSID",
        @"Classes\WOW6432Node",
        @"Classes\Interface",
        @"Classes\TypeLib",
    ];

    public async Task AnalyzeAsync(InstalledProgram program, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var installPath = program.InstallLocation.TrimEnd('\\');
        if (string.IsNullOrWhiteSpace(installPath))
            throw new ArgumentException($"程序 {program.Name} 的安装路径为空", nameof(program));

        // 使用线程安全的临时列表收集引用，避免后台线程操作 ObservableCollection
        var tempProgram = new InstalledProgram
        {
            Name = program.Name,
            InstallLocation = program.InstallLocation,
            Publisher = program.Publisher,
            UninstallString = program.UninstallString,
            RegistryKeyPath = program.RegistryKeyPath,
            SizeBytes = program.SizeBytes,
        };

        await Task.Run(() =>
        {
            progress?.Report($"[{program.Name}] 扫描注册表引用...");
            ScanProgramUninstallKey(tempProgram, installPath);
            ScanKnownRegistryLocations(tempProgram, installPath, progress, ct);
            ScanRegistryDeep(tempProgram, installPath, progress, ct);

            progress?.Report($"[{program.Name}] 扫描快捷方式...");
            ScanShortcuts(tempProgram, installPath, progress, ct);

            progress?.Report($"[{program.Name}] 扫描服务...");
            ScanServices(tempProgram, installPath, progress, ct);

            progress?.Report($"[{program.Name}] 扫描环境变量...");
            ScanEnvironmentVariables(tempProgram, installPath);

            progress?.Report($"[{program.Name}] 扫描计划任务...");
            ScanScheduledTasks(tempProgram, installPath, progress, ct);

            progress?.Report($"[{program.Name}] 扫描内部配置文件...");
            ScanConfigFiles(tempProgram, installPath, progress, ct);

            progress?.Report($"[{program.Name}] 评估迁移风险...");
            AssessRisk(tempProgram);
            ReassessRecommendation(tempProgram);
        }, ct);

        // 通过 Dispatcher 安全写入 ObservableCollection（兼容后台线程调用）
        var refs = tempProgram.References.ToList();
        Action writeBack = () =>
        {
            program.References.Clear();
            foreach (var r in refs)
                program.References.Add(r);
            program.Risk = tempProgram.Risk;
            program.RiskReason = tempProgram.RiskReason;
            program.Recommendation = tempProgram.Recommendation;
            program.RecommendReason = tempProgram.RecommendReason;
            program.IsAnalyzed = true;
        };

        if (System.Windows.Application.Current?.Dispatcher.CheckAccess() == true)
            writeBack();
        else
            System.Windows.Application.Current?.Dispatcher.Invoke(writeBack);
    }

    #region Registry Scanning

    private static void ScanProgramUninstallKey(InstalledProgram program, string installPath)
    {
        if (string.IsNullOrEmpty(program.RegistryKeyPath)) return;

        try
        {
            var parts = SplitRegistryPath(program.RegistryKeyPath);
            if (parts == null) return;

            using var key = parts.Value.root.OpenSubKey(parts.Value.subKey);
            if (key == null) return;

            foreach (var valName in key.GetValueNames())
            {
                var val = key.GetValue(valName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (val is string strVal && ContainsPath(strVal, installPath))
                {
                    program.References.Add(new ProgramReference
                    {
                        Type = ReferenceType.RegistryValue,
                        Location = program.RegistryKeyPath,
                        ValueName = valName,
                        OriginalValue = strVal,
                        Description = $"卸载注册表项: {valName}"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[{program.Name}] 扫描卸载注册表项失败: {ex.Message}");
        }
    }

    private static void ScanKnownRegistryLocations(InstalledProgram program, string installPath,
        IProgress<string>? progress, CancellationToken ct)
    {
        foreach (var rootPath in RegistrySearchRoots)
        {
            ct.ThrowIfCancellationRequested();
            ScanRegistryKeyRecursive(Registry.LocalMachine, rootPath, program, installPath, 2, ct);
            ScanRegistryKeyRecursive(Registry.CurrentUser, rootPath, program, installPath, 2, ct);
        }

        // Scan HKLM\SYSTEM\CurrentControlSet\Services for this program
        ScanRegistryKeyRecursive(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services",
            program, installPath, 3, ct);

        // Scan HKCR for COM registrations (depth 2 only — CLSID is huge)
        ScanRegistryKeyRecursive(Registry.ClassesRoot, @"CLSID", program, installPath, 2, ct);
        ScanRegistryKeyRecursive(Registry.ClassesRoot, @"WOW6432Node\CLSID", program, installPath, 2, ct);
    }

    private static void ScanRegistryDeep(InstalledProgram program, string installPath,
        IProgress<string>? progress, CancellationToken ct)
    {
        // 只扫描可能包含程序路径的特定注册表子树（避免全树扫描）
        var targetPaths = new (RegistryKey root, string path, int depth)[]
        {
            // 程序注册信息
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths", 2),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs", 1),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\Folders", 1),
            // 文件关联
            (Registry.LocalMachine, @"SOFTWARE\Classes\Applications", 3),
            (Registry.CurrentUser, @"SOFTWARE\Classes\Applications", 3),
            // 程序自身的注册表键（按程序名搜索）
            (Registry.LocalMachine, @"SOFTWARE", 2),
            (Registry.CurrentUser, @"SOFTWARE", 2),
        };

        // 尝试直接按程序名定位注册表键（最快路径）
        var progName = Path.GetFileNameWithoutExtension(
            program.Name.Split(new[] { ' ', '-', '_' }, 2)[0]);
        if (progName.Length >= 3)
        {
            var directPaths = new[]
            {
                $@"SOFTWARE\{progName}",
                $@"SOFTWARE\{program.Publisher ?? ""}",
            };
            foreach (var dp in directPaths.Where(p => !string.IsNullOrWhiteSpace(p.Split('\\').Last())))
            {
                ScanRegistryKeyRecursive(Registry.LocalMachine, dp, program, installPath, 4, ct);
                ScanRegistryKeyRecursive(Registry.CurrentUser, dp, program, installPath, 4, ct);
            }
        }

        foreach (var (root, path, depth) in targetPaths)
        {
            ct.ThrowIfCancellationRequested();
            ScanRegistryKeyRecursive(root, path, program, installPath, depth, ct);
        }
    }

    private static void ScanRegistryKeyRecursive(RegistryKey rootKey, string keyPath,
        InstalledProgram program, string installPath, int maxDepth, CancellationToken ct, int depth = 0)
    {
        if (depth > maxDepth) return;
        ct.ThrowIfCancellationRequested();

        // Skip known huge/irrelevant keys
        foreach (var skip in RegistryDeepScanSkip)
        {
            if (keyPath.Contains(skip, StringComparison.OrdinalIgnoreCase)) return;
        }

        try
        {
            using var key = rootKey.OpenSubKey(keyPath);
            if (key == null) return;

            // Check values
            foreach (var valName in key.GetValueNames())
            {
                try
                {
                    var kind = key.GetValueKind(valName);
                    if (kind != RegistryValueKind.String && kind != RegistryValueKind.ExpandString)
                        continue;

                    var val = key.GetValue(valName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if (val == null || !ContainsPath(val, installPath)) continue;

                    var fullKeyPath = $"{rootKey.Name}\\{keyPath}";

                    // Avoid duplicates
                    if (program.References.Any(r =>
                        r.Type == ReferenceType.RegistryValue &&
                        r.Location == fullKeyPath &&
                        r.ValueName == valName))
                        continue;

                    program.References.Add(new ProgramReference
                    {
                        Type = ReferenceType.RegistryValue,
                        Location = fullKeyPath,
                        ValueName = valName,
                        OriginalValue = val,
                        Description = $"注册表: {keyPath}\\{valName}"
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Access denied for individual values is expected — skip silently
                }
            }

            // Recurse into subkeys
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                ScanRegistryKeyRecursive(rootKey, $@"{keyPath}\{subKeyName}", program, installPath, maxDepth, ct, depth + 1);
            }
        }
        catch (System.Security.SecurityException) { }
        catch (UnauthorizedAccessException) { }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"扫描注册表键失败 {keyPath}: {ex.Message}");
        }
    }

    #endregion

    #region Shortcut Scanning

    private static void ScanShortcuts(InstalledProgram program, string installPath,
        IProgress<string>? progress, CancellationToken ct)
    {
        var searchDirs = new List<string>();

        // All users desktop
        var allUsersDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        if (!string.IsNullOrEmpty(allUsersDesktop)) searchDirs.Add(allUsersDesktop);

        // Current user desktop
        var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrEmpty(userDesktop)) searchDirs.Add(userDesktop);

        // All users start menu
        var allUsersStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        if (!string.IsNullOrEmpty(allUsersStartMenu)) searchDirs.Add(allUsersStartMenu);

        // Current user start menu
        var userStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        if (!string.IsNullOrEmpty(userStartMenu)) searchDirs.Add(userStartMenu);

        // Quick Launch
        var quickLaunch = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Internet Explorer\Quick Launch");
        if (Directory.Exists(quickLaunch)) searchDirs.Add(quickLaunch);

        foreach (var dir in searchDirs)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(dir)) continue;

            try
            {
                foreach (var lnkFile in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var target = GetShortcutTarget(lnkFile);
                        if (target != null && ContainsPath(target, installPath))
                        {
                            program.References.Add(new ProgramReference
                            {
                                Type = ReferenceType.Shortcut,
                                Location = lnkFile,
                                OriginalValue = target,
                                Description = $"快捷方式: {Path.GetFileNameWithoutExtension(lnkFile)}"
                            });
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        System.Diagnostics.Debug.WriteLine($"读取快捷方式失败 {lnkFile}: {ex.Message}");
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                progress?.Report($"[扫描快捷方式] 目录 {dir} 访问失败: {ex.Message}");
            }
        }
    }

    private static string? GetShortcutTarget(string lnkPath)
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

    #endregion

    #region Services Scanning

    private static void ScanServices(InstalledProgram program, string installPath,
        IProgress<string>? progress, CancellationToken ct)
    {
        try
        {
            using var scKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (scKey == null) return;

            foreach (var svcName in scKey.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var svcKey = scKey.OpenSubKey(svcName);
                    if (svcKey == null) continue;

                    var imagePath = svcKey.GetValue("ImagePath") as string;
                    if (imagePath == null || !ContainsPath(imagePath, installPath)) continue;

                    // Avoid duplicates with registry scan
                    var fullPath = $@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\{svcName}";
                    if (program.References.Any(r => r.Location == fullPath && r.ValueName == "ImagePath"))
                        continue;

                    program.References.Add(new ProgramReference
                    {
                        Type = ReferenceType.WindowsService,
                        Location = fullPath,
                        ValueName = svcName,
                        OriginalValue = imagePath,
                        Description = $"服务: {svcKey.GetValue("DisplayName") as string ?? svcName}"
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine($"扫描服务 {svcName} 失败: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            progress?.Report($"[扫描服务] 访问服务注册表失败: {ex.Message}");
        }
    }

    #endregion

    #region Environment Variable Scanning

    private static void ScanEnvironmentVariables(InstalledProgram program, string installPath)
    {
        // System PATH
        CheckEnvVar(program, installPath, "Path", EnvironmentVariableTarget.Machine, "系统PATH");
        // User PATH
        CheckEnvVar(program, installPath, "Path", EnvironmentVariableTarget.User, "用户PATH");

        // Check all system env vars
        try
        {
            using var envKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment");
            if (envKey != null)
            {
                foreach (var name in envKey.GetValueNames())
                {
                    if (name.Equals("Path", StringComparison.OrdinalIgnoreCase)) continue;
                    var val = envKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if (val != null && ContainsPath(val, installPath))
                    {
                        program.References.Add(new ProgramReference
                        {
                            Type = ReferenceType.EnvironmentVariable,
                            Location = "HKLM\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment",
                            ValueName = name,
                            OriginalValue = val,
                            Description = $"系统环境变量: {name}"
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"扫描系统环境变量失败: {ex.Message}");
        }

        // Check all user env vars
        try
        {
            using var envKey = Registry.CurrentUser.OpenSubKey(@"Environment");
            if (envKey != null)
            {
                foreach (var name in envKey.GetValueNames())
                {
                    if (name.Equals("Path", StringComparison.OrdinalIgnoreCase)) continue;
                    var val = envKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
                    if (val != null && ContainsPath(val, installPath))
                    {
                        program.References.Add(new ProgramReference
                        {
                            Type = ReferenceType.EnvironmentVariable,
                            Location = "HKCU\\Environment",
                            ValueName = name,
                            OriginalValue = val,
                            Description = $"用户环境变量: {name}"
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"扫描用户环境变量失败: {ex.Message}");
        }
    }

    private static void CheckEnvVar(InstalledProgram program, string installPath,
        string varName, EnvironmentVariableTarget target, string desc)
    {
        var value = Environment.GetEnvironmentVariable(varName, target);
        if (value == null) return;

        var parts = value.Split(';');
        foreach (var part in parts)
        {
            if (ContainsPath(part.Trim(), installPath))
            {
                var location = target == EnvironmentVariableTarget.Machine
                    ? "HKLM\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment"
                    : "HKCU\\Environment";

                program.References.Add(new ProgramReference
                {
                    Type = ReferenceType.EnvironmentVariable,
                    Location = location,
                    ValueName = varName,
                    OriginalValue = value,
                    Description = $"{desc}: 包含 {part.Trim()}"
                });
                break;
            }
        }
    }

    #endregion

    #region Scheduled Tasks Scanning

    public void PrewarmCaches(CancellationToken ct) => EnsureScheduledTasksCache(ct);

    private void EnsureScheduledTasksCache(CancellationToken ct)
    {
        lock (_taskCacheLock)
        {
            if (_scheduledTasksCache != null) return;
            _scheduledTasksCache = new List<string>();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/query /fo CSV /v /nh",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    System.Diagnostics.Debug.WriteLine("无法启动 schtasks.exe 查询计划任务");
                    return;
                }
                using var reader = proc.StandardOutput;

                while (!reader.EndOfStream)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = reader.ReadLine();
                    if (!string.IsNullOrEmpty(line))
                        _scheduledTasksCache.Add(line);
                }
                proc.WaitForExit(15000);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"查询计划任务失败: {ex.Message}");
            }
        }
    }

    private void ScanScheduledTasks(InstalledProgram program, string installPath,
        IProgress<string>? progress, CancellationToken ct)
    {
        EnsureScheduledTasksCache(ct);

        foreach (var line in _scheduledTasksCache!)
        {
            if (ContainsPath(line, installPath))
            {
                var fields = ParseCsvLine(line);
                var taskName = fields.Count > 1 ? fields[1] : "未知任务";
                var taskAction = fields.Count > 8 ? fields[8] : line;

                if (!program.References.Any(r => r.Type == ReferenceType.ScheduledTask && r.ValueName == taskName))
                {
                    program.References.Add(new ProgramReference
                    {
                        Type = ReferenceType.ScheduledTask,
                        Location = "任务计划程序",
                        ValueName = taskName,
                        OriginalValue = taskAction,
                        Description = $"计划任务: {taskName}"
                    });
                }
            }
        }
    }

    #endregion

    #region Config File Scanning

    private static void ScanConfigFiles(InstalledProgram program, string installPath,
        IProgress<string>? progress, CancellationToken ct)
    {
        if (!Directory.Exists(installPath)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(installPath, "*.*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 3
            }))
            {
                ct.ThrowIfCancellationRequested();
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!ConfigExtensions.Contains(ext)) continue;

                try
                {
                    var fi = new FileInfo(file);
                    if (fi.Length > 10 * 1024 * 1024) continue; // skip files > 10MB

                    var content = File.ReadAllText(file);
                    if (ContainsPath(content, installPath))
                    {
                        program.References.Add(new ProgramReference
                        {
                            Type = ReferenceType.ConfigFile,
                            Location = file,
                            OriginalValue = installPath,
                            Description = $"配置文件: {Path.GetRelativePath(installPath, file)}"
                        });
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine($"读取配置文件失败 {file}: {ex.Message}");
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            progress?.Report($"[{program.Name}] 扫描配置文件失败: {ex.Message}");
        }
    }

    #endregion

    #region Risk Assessment

    public static void AssessRisk(InstalledProgram program)
    {
        var path = program.InstallLocation.ToLowerInvariant();
        var name = program.Name.ToLowerInvariant();

        // Critical: System components, drivers, core Windows features
        if (path.StartsWith(@"c:\windows") ||
            name.Contains("windows") && name.Contains("sdk") ||
            name.Contains("visual c++ redistributable") ||
            name.Contains(".net framework") ||
            name.Contains(".net runtime") ||
            name.Contains("visual studio build tools"))
        {
            program.Risk = RiskLevel.Critical;
            program.RiskReason = "系统核心组件，迁移可能导致系统不稳定";
            return;
        }

        // High: Has services or scheduled tasks
        var hasServices = program.References.Any(r => r.Type == ReferenceType.WindowsService);
        var hasScheduledTasks = program.References.Any(r => r.Type == ReferenceType.ScheduledTask);
        var regCount = program.References.Count(r => r.Type == ReferenceType.RegistryValue);

        if (hasServices && hasScheduledTasks)
        {
            program.Risk = RiskLevel.High;
            program.RiskReason = $"包含 Windows 服务和计划任务，共 {program.References.Count} 个引用需要修复";
            return;
        }

        if (hasServices)
        {
            program.Risk = RiskLevel.Medium;
            program.RiskReason = $"包含 Windows 服务，需要修复服务路径";
            return;
        }

        if (regCount > 50)
        {
            program.Risk = RiskLevel.Medium;
            program.RiskReason = $"注册表引用较多 ({regCount} 个)，迁移时间可能较长";
            return;
        }

        if (hasScheduledTasks)
        {
            program.Risk = RiskLevel.Low;
            program.RiskReason = "包含计划任务引用";
            return;
        }

        if (regCount > 10)
        {
            program.Risk = RiskLevel.Low;
            program.RiskReason = $"有 {regCount} 个注册表引用";
            return;
        }

        program.Risk = RiskLevel.Safe;
        program.RiskReason = $"引用简单 ({program.References.Count} 个引用)，迁移安全";
    }

    /// <summary>
    /// 根据分析后的实际引用数据重新评估迁移建议
    /// </summary>
    public static void ReassessRecommendation(InstalledProgram program)
    {
        switch (program.Risk)
        {
            case RiskLevel.Critical:
                program.Recommendation = Recommendation.NotRecommended;
                program.RecommendReason = $"⛔ {program.RiskReason}";
                break;
            case RiskLevel.High:
                program.Recommendation = Recommendation.NotRecommended;
                program.RecommendReason = $"⚠ 高风险: {program.RiskReason}";
                break;
            case RiskLevel.Medium:
                // 有服务的降为 Caution，纯注册表多的保持 Recommended
                if (program.References.Any(r => r.Type == ReferenceType.WindowsService))
                {
                    program.Recommendation = Recommendation.Caution;
                    program.RecommendReason = $"含服务引用，需谨慎: {program.RiskReason}";
                }
                else
                {
                    program.Recommendation = Recommendation.Recommended;
                    program.RecommendReason = $"可迁移（注册表引用较多会自动修复）: {program.RiskReason}";
                }
                break;
            case RiskLevel.Low:
            case RiskLevel.Safe:
                program.Recommendation = Recommendation.Recommended;
                program.RecommendReason = $"✓ 安全迁移: {program.RiskReason}";
                break;
        }
    }

    #endregion

    #region Helpers

    private static bool ContainsPath(string text, string path)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(path)) return false;

        // Direct match
        if (text.Contains(path, StringComparison.OrdinalIgnoreCase)) return true;

        // Match with trailing backslash
        if (text.Contains(path + "\\", StringComparison.OrdinalIgnoreCase)) return true;

        // Match forward slashes
        var fwdPath = path.Replace('\\', '/');
        if (text.Contains(fwdPath, StringComparison.OrdinalIgnoreCase)) return true;

        // Match quoted paths
        var quotedPath = "\"" + path;
        if (text.Contains(quotedPath, StringComparison.OrdinalIgnoreCase)) return true;

        return false;
    }

    private static (RegistryKey root, string subKey)? SplitRegistryPath(string fullPath)
    {
        RegistryKey? root = null;
        string subKey;

        if (fullPath.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
        {
            root = Registry.LocalMachine;
            subKey = fullPath["HKEY_LOCAL_MACHINE\\".Length..];
        }
        else if (fullPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
        {
            root = Registry.CurrentUser;
            subKey = fullPath["HKEY_CURRENT_USER\\".Length..];
        }
        else if (fullPath.StartsWith("HKEY_CLASSES_ROOT\\", StringComparison.OrdinalIgnoreCase))
        {
            root = Registry.ClassesRoot;
            subKey = fullPath["HKEY_CLASSES_ROOT\\".Length..];
        }
        else
        {
            return null;
        }

        return (root, subKey);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var inQuotes = false;
        var current = "";

        foreach (var c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current);
                current = "";
            }
            else
            {
                current += c;
            }
        }
        fields.Add(current);
        return fields;
    }

    #endregion
}
