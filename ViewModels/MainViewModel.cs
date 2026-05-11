using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using CDriveMigrator.Helpers;
using CDriveMigrator.Models;
using CDriveMigrator.Services;

namespace CDriveMigrator.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ProgramScanner _scanner = new();
    private readonly ReferenceAnalyzer _analyzer = new();
    private readonly MigrationEngine _engine = new();

    private CancellationTokenSource? _cts;
    private InstalledProgram? _selectedProgram;
    private string _selectedDrive = "D:\\";
    private string _targetSubFolder = "MigratedPrograms";
    private bool _isBusy;
    private double _progress;
    private string _statusText = "就绪 — 点击「扫描」开始";
    private int _totalPrograms;
    private long _totalSize;
    private int _selectedCount;
    private long _selectedSize;
    private int _safeCount;
    private int _cautionCount;
    private int _notRecommendedCount;

    public MainViewModel()
    {
        ScanCommand = new AsyncRelayCommand(ScanAsync, () => !IsBusy);
        AnalyzeSelectedCommand = new AsyncRelayCommand(AnalyzeSelectedAsync, () => !IsBusy && SelectedProgram != null);
        AnalyzeAllCommand = new AsyncRelayCommand(AnalyzeAllAsync, () => !IsBusy && Programs.Count > 0);
        MigrateCommand = new AsyncRelayCommand(MigrateAsync, () => !IsBusy && Programs.Any(p => p.IsSelected));
        UninstallCommand = new AsyncRelayCommand(UninstallAsync, () => !IsBusy && Programs.Any(p => p.IsSelected));
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        SelectAllCommand = new RelayCommand(SelectAll, () => Programs.Count > 0);
        DeselectAllCommand = new RelayCommand(DeselectAll, () => Programs.Count > 0);
        SelectRecommendedCommand = new RelayCommand(SelectRecommended, () => Programs.Count > 0);
        SelectCautionCommand = new RelayCommand(SelectCaution, () => Programs.Count > 0);
        SelectNotRecommendedCommand = new RelayCommand(SelectNotRecommended, () => Programs.Count > 0);

        _engine.Log += msg => Application.Current?.Dispatcher.Invoke(() =>
        {
            LogMessages.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
            while (LogMessages.Count > 2000) LogMessages.RemoveAt(0);
        });

        _engine.ProgressChanged += p => Application.Current?.Dispatcher.Invoke(() => Progress = p);

        LoadAvailableDrives();
    }

    #region Properties

    public ObservableCollection<InstalledProgram> Programs { get; } = new();
    public ObservableCollection<string> LogMessages { get; } = new();
    public ObservableCollection<string> AvailableDrives { get; } = new();
    public ObservableCollection<MigrationResult> Results { get; } = new();

    public InstalledProgram? SelectedProgram
    {
        get => _selectedProgram;
        set { _selectedProgram = value; OnPropertyChanged(); }
    }

    public string SelectedDrive
    {
        get => _selectedDrive;
        set { _selectedDrive = value; OnPropertyChanged(); OnPropertyChanged(nameof(TargetPath)); }
    }

    public string TargetSubFolder
    {
        get => _targetSubFolder;
        set { _targetSubFolder = value; OnPropertyChanged(); OnPropertyChanged(nameof(TargetPath)); }
    }

    public string TargetPath => Path.Combine(SelectedDrive, TargetSubFolder);

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public double Progress
    {
        get => _progress;
        set { _progress = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public int TotalPrograms
    {
        get => _totalPrograms;
        set { _totalPrograms = value; OnPropertyChanged(); }
    }

    public long TotalSize
    {
        get => _totalSize;
        set { _totalSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalSizeDisplay)); }
    }

    public string TotalSizeDisplay => InstalledProgram.FormatSize(TotalSize);

    public int SelectedCount
    {
        get => _selectedCount;
        set { _selectedCount = value; OnPropertyChanged(); }
    }

    public long SelectedSize
    {
        get => _selectedSize;
        set { _selectedSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedSizeDisplay)); }
    }

    public string SelectedSizeDisplay => InstalledProgram.FormatSize(SelectedSize);

    public int SafeCount
    {
        get => _safeCount;
        set { _safeCount = value; OnPropertyChanged(); }
    }

    public int CautionCount
    {
        get => _cautionCount;
        set { _cautionCount = value; OnPropertyChanged(); }
    }

    public int NotRecommendedCount
    {
        get => _notRecommendedCount;
        set { _notRecommendedCount = value; OnPropertyChanged(); }
    }

    #endregion

    #region Commands

    public AsyncRelayCommand ScanCommand { get; }
    public AsyncRelayCommand AnalyzeSelectedCommand { get; }
    public AsyncRelayCommand AnalyzeAllCommand { get; }
    public AsyncRelayCommand MigrateCommand { get; }
    public AsyncRelayCommand UninstallCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand DeselectAllCommand { get; }
    public RelayCommand SelectRecommendedCommand { get; }
    public RelayCommand SelectCautionCommand { get; }
    public RelayCommand SelectNotRecommendedCommand { get; }

    #endregion

    #region Command Implementations

    private async Task ScanAsync()
    {
        try
        {
            IsBusy = true;
            Progress = 0;
            StatusText = "正在扫描已安装程序...";
            Programs.Clear();
            LogMessages.Clear();
            AddLog("开始扫描 C 盘已安装程序...");

            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            var progressReporter = new Progress<string>(msg =>
            {
                try { StatusText = msg; AddLog(msg); } catch { }
            });

            var programs = await _scanner.ScanAsync(progressReporter, _cts.Token);

            foreach (var p in programs)
            {
                p.PropertyChanged += Program_PropertyChanged;
                Programs.Add(p);
            }

            TotalPrograms = Programs.Count;
            TotalSize = Programs.Sum(p => p.SizeBytes);
            SafeCount = Programs.Count(p => p.Recommendation == Recommendation.Recommended);
            CautionCount = Programs.Count(p => p.Recommendation == Recommendation.Caution);
            NotRecommendedCount = Programs.Count(p => p.Recommendation == Recommendation.NotRecommended);

            AddLog($"扫描完成：发现 {TotalPrograms} 个程序，总占用 {TotalSizeDisplay}");
            AddLog($"  建议迁移: {SafeCount} 个 | 需分析: {CautionCount} 个 | 不建议: {NotRecommendedCount} 个");
            StatusText = $"扫描完成，开始自动分析引用...";

            // 自动分析所有程序
            await AutoAnalyzeAllAsync(_cts.Token);

            // 更新统计
            SafeCount = Programs.Count(p => p.Recommendation == Recommendation.Recommended);
            CautionCount = Programs.Count(p => p.Recommendation == Recommendation.Caution);
            NotRecommendedCount = Programs.Count(p => p.Recommendation == Recommendation.NotRecommended);

            AddLog($"全部分析完成 — 建议迁移: {SafeCount} | 需分析: {CautionCount} | 不建议: {NotRecommendedCount}");
            StatusText = $"分析完成 — {SafeCount} 个建议迁移，{CautionCount} 个需分析，{NotRecommendedCount} 个不建议";
        }
        catch (OperationCanceledException)
        {
            StatusText = "扫描已取消";
        }
        catch (Exception ex)
        {
            AddLog($"扫描出错: {ex.Message}\n{ex.StackTrace}");
            StatusText = $"扫描出错: {ex.Message}";
            MessageBox.Show($"扫描出错:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "CDriveMigrator", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            Progress = 100;
        }
    }

    private async Task AutoAnalyzeAllAsync(CancellationToken ct)
    {
        int completed = 0;
        int total = Programs.Count;
        int concurrency = Math.Max(2, Environment.ProcessorCount); // 按CPU核心数并发

        AddLog($"并发分析: {concurrency} 线程同时处理 {total} 个程序...");

        // 预热缓存（schtasks只执行一次）
        await Task.Run(() => _analyzer.PrewarmCaches(ct), ct);

        // 先标记所有为分析中
        foreach (var p in Programs)
            p.Status = MigrationStatus.Analyzing;

        var semaphore = new SemaphoreSlim(concurrency);
        var tasks = Programs.Select(program => Task.Run(async () =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();

                await _analyzer.AnalyzeAsync(program, null, ct);

                var done = Interlocked.Increment(ref completed);
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        program.Status = MigrationStatus.Pending;
                        Progress = (double)done / total * 100;
                        StatusText = $"分析 ({done}/{total}): {program.Name} — {program.References.Count} 引用";
                    }
                    catch { }
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Interlocked.Increment(ref completed);
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        program.Status = MigrationStatus.Pending;
                        AddLog($"  ⚠ {program.Name} 分析失败: {ex.Message}");
                    }
                    catch { }
                });
            }
            finally
            {
                semaphore.Release();
            }
        }, ct)).ToArray();

        await Task.WhenAll(tasks);

        // 汇总日志
        foreach (var p in Programs)
            AddLog($"  → {p.Name}: {p.References.Count} 引用, {p.RiskDisplay}, {p.RecommendDisplay}");
    }

    private async Task AnalyzeSelectedAsync()
    {
        if (SelectedProgram == null) return;

        IsBusy = true;
        Progress = 0;
        var program = SelectedProgram;
        program.Status = MigrationStatus.Analyzing;
        StatusText = $"正在分析: {program.Name}";
        AddLog($"开始分析: {program.Name} ({program.InstallLocation})");

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        try
        {
            await _analyzer.AnalyzeAsync(program, new Progress<string>(msg =>
            {
                try { StatusText = msg; AddLog(msg); } catch { }
            }), _cts.Token);

            program.Status = MigrationStatus.Pending;
            AddLog($"分析完成: {program.Name} — {program.References.Count} 个引用，风险: {program.RiskDisplay}，建议: {program.RecommendDisplay}");
            StatusText = $"分析完成 — {program.References.Count} 个引用，{program.RecommendDisplay}";
            OnPropertyChanged(nameof(SelectedProgram));
        }
        catch (OperationCanceledException)
        {
            program.Status = MigrationStatus.Pending;
            StatusText = "分析已取消";
        }
        catch (Exception ex)
        {
            program.Status = MigrationStatus.Pending;
            AddLog($"分析出错: {ex.Message}");
            StatusText = $"分析出错: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Progress = 100;
        }
    }

    private async Task AnalyzeAllAsync()
    {
        IsBusy = true;
        Progress = 0;
        StatusText = "正在分析所有程序...";
        AddLog("开始批量分析...");

        _cts = new CancellationTokenSource();
        try
        {
            await AutoAnalyzeAllAsync(_cts.Token);

            SafeCount = Programs.Count(p => p.Recommendation == Recommendation.Recommended);
            CautionCount = Programs.Count(p => p.Recommendation == Recommendation.Caution);
            NotRecommendedCount = Programs.Count(p => p.Recommendation == Recommendation.NotRecommended);

            AddLog($"批量分析完成 — 建议迁移: {SafeCount} | 需分析: {CautionCount} | 不建议: {NotRecommendedCount}");
            StatusText = $"分析完成 — {SafeCount} 个建议迁移，{CautionCount} 个需分析，{NotRecommendedCount} 个不建议";
        }
        catch (OperationCanceledException)
        {
            StatusText = "分析已取消";
        }
        catch (Exception ex)
        {
            AddLog($"分析出错: {ex.Message}");
            StatusText = $"分析出错: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Progress = 100;
        }
    }

    private async Task MigrateAsync()
    {
        var selected = Programs.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return;

        // Confirm with user
        var criticalCount = selected.Count(p => p.Risk == RiskLevel.Critical);
        var highCount = selected.Count(p => p.Risk == RiskLevel.High);

        if (criticalCount > 0)
        {
            var mbResult = MessageBox.Show(
                $"选中的程序中有 {criticalCount} 个被标记为「危险」级别，迁移可能导致系统不稳定。\n\n确定要继续吗？",
                "高风险警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (mbResult != MessageBoxResult.Yes) return;
        }
        else if (highCount > 0)
        {
            var mbResult = MessageBox.Show(
                $"选中 {selected.Count} 个程序（其中 {highCount} 个高风险）。\n" +
                $"目标路径: {TargetPath}\n\n确定开始迁移吗？",
                "确认迁移", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (mbResult != MessageBoxResult.Yes) return;
        }
        else
        {
            var mbResult = MessageBox.Show(
                $"即将迁移 {selected.Count} 个程序到 {TargetPath}\n" +
                $"预计释放空间: {InstalledProgram.FormatSize(selected.Sum(p => p.SizeBytes))}\n\n开始迁移？",
                "确认迁移", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (mbResult != MessageBoxResult.Yes) return;
        }

        IsBusy = true;
        Progress = 0;
        Results.Clear();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            for (int i = 0; i < selected.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                var program = selected[i];

                StatusText = $"迁移 ({i + 1}/{selected.Count}): {program.Name}";
                AddLog($"========== 迁移: {program.Name} ==========");

                var result = await _engine.MigrateAsync(program, TargetPath, _cts.Token);
                Results.Add(result);

                if (result.Success)
                    AddLog($"✓ {program.Name} 迁移成功 — 释放 {InstalledProgram.FormatSize(result.SpaceSaved)}");
                else
                    AddLog($"✗ {program.Name} 迁移失败 — {result.Message}");

                Progress = (double)(i + 1) / selected.Count * 100;
            }

            var successCount = Results.Count(r => r.Success);
            var totalSaved = Results.Where(r => r.Success).Sum(r => r.SpaceSaved);
            StatusText = $"迁移完成 — 成功 {successCount}/{selected.Count}，释放 {InstalledProgram.FormatSize(totalSaved)}";
            AddLog($"\n======= 迁移汇总 =======");
            AddLog($"成功: {successCount}/{selected.Count}");
            AddLog($"释放空间: {InstalledProgram.FormatSize(totalSaved)}");

            MessageBox.Show(
                $"迁移完成！\n\n成功: {successCount}/{selected.Count}\n释放空间: {InstalledProgram.FormatSize(totalSaved)}",
                "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusText = "迁移已取消";
            AddLog("迁移被用户取消");
        }
        catch (Exception ex)
        {
            StatusText = $"迁移出错: {ex.Message}";
            AddLog($"迁移出错: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "正在取消...";
        AddLog("用户请求取消操作...");
    }

    private async Task UninstallAsync()
    {
        var selected = Programs.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0) return;

        var names = string.Join("\n", selected.Select(p => $"  • {p.Name}"));
        var mbResult = MessageBox.Show(
            $"⚠ 彻底卸载将执行以下操作：\n\n" +
            $"1. 强制结束所有相关进程\n" +
            $"2. 删除安装目录及全部文件\n" +
            $"3. 清理注册表卸载项\n" +
            $"4. 删除桌面和开始菜单快捷方式\n\n" +
            $"即将卸载 {selected.Count} 个程序：\n{names}\n\n" +
            $"此操作不可逆！确定继续？",
            "彻底卸载确认", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (mbResult != MessageBoxResult.Yes) return;

        IsBusy = true;
        Progress = 0;
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            int successCount = 0;
            long totalFreed = 0;

            for (int i = 0; i < selected.Count; i++)
            {
                _cts.Token.ThrowIfCancellationRequested();
                var program = selected[i];
                Progress = (double)i / selected.Count * 100;
                StatusText = $"卸载 ({i + 1}/{selected.Count}): {program.Name}";
                AddLog($"========== 彻底卸载: {program.Name} ==========");

                try
                {
                    long freed = await ForceUninstallAsync(program, _cts.Token);
                    successCount++;
                    totalFreed += freed;
                    AddLog($"✓ {program.Name} 卸载完成，释放 {InstalledProgram.FormatSize(freed)}");
                    // 从列表中移除已卸载程序
                    Programs.Remove(program);
                }
                catch (Exception ex)
                {
                    program.Status = MigrationStatus.Failed;
                    AddLog($"✗ {program.Name} 卸载失败: {ex.Message}");
                }
            }

            // 更新统计
            TotalPrograms = Programs.Count;
            TotalSize = Programs.Sum(p => p.SizeBytes);
            SafeCount = Programs.Count(p => p.Recommendation == Recommendation.Recommended);
            CautionCount = Programs.Count(p => p.Recommendation == Recommendation.Caution);
            NotRecommendedCount = Programs.Count(p => p.Recommendation == Recommendation.NotRecommended);
            UpdateSelectionStats();

            StatusText = $"卸载完成 — 成功 {successCount}/{selected.Count}，释放 {InstalledProgram.FormatSize(totalFreed)}";
            AddLog($"\n======= 卸载汇总 =======");
            AddLog($"成功: {successCount}/{selected.Count}，释放: {InstalledProgram.FormatSize(totalFreed)}");

            MessageBox.Show(
                $"卸载完成！\n\n成功: {successCount}/{selected.Count}\n释放空间: {InstalledProgram.FormatSize(totalFreed)}",
                "完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            StatusText = "卸载已取消";
        }
        catch (Exception ex)
        {
            StatusText = $"卸载出错: {ex.Message}";
            AddLog($"卸载出错: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
            Progress = 100;
        }
    }

    private async Task<long> ForceUninstallAsync(InstalledProgram program, CancellationToken ct)
    {
        long sizeFreed = program.SizeBytes;
        var installPath = program.InstallLocation;

        await Task.Run(() =>
        {
            // 1. 强制结束安装目录下的所有进程
            AddLog($"  [1/4] 结束相关进程...");
            try
            {
                foreach (var proc in Process.GetProcesses())
                {
                    try
                    {
                        var exePath = proc.MainModule?.FileName;
                        if (exePath != null && exePath.StartsWith(installPath, StringComparison.OrdinalIgnoreCase))
                        {
                            AddLog($"    终止进程: {proc.ProcessName} (PID: {proc.Id})");
                            proc.Kill(true);
                            proc.WaitForExit(5000);
                        }
                    }
                    catch { }
                }
            }
            catch { }

            ct.ThrowIfCancellationRequested();

            // 2. 删除安装目录
            AddLog($"  [2/4] 删除安装目录: {installPath}");
            if (Directory.Exists(installPath))
            {
                try
                {
                    // 先去除只读属性
                    foreach (var fi in new DirectoryInfo(installPath).EnumerateFiles("*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true
                    }))
                    {
                        try { fi.Attributes = FileAttributes.Normal; } catch { }
                    }
                    Directory.Delete(installPath, true);
                    AddLog($"    ✓ 目录已删除");
                }
                catch (Exception ex)
                {
                    AddLog($"    ⚠ 部分文件删除失败: {ex.Message}");
                    // 尝试用 cmd 强制删除
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "cmd.exe",
                            Arguments = $"/c rd /s /q \"{installPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var p = Process.Start(psi);
                        p?.WaitForExit(30000);
                    }
                    catch { }
                }
            }

            ct.ThrowIfCancellationRequested();

            // 3. 清理注册表
            AddLog($"  [3/4] 清理注册表...");
            CleanRegistry(program);

            // 4. 删除快捷方式
            AddLog($"  [4/4] 清理快捷方式...");
            CleanShortcuts(program);

        }, ct);

        return sizeFreed;
    }

    private void CleanRegistry(InstalledProgram program)
    {
        // 删除卸载注册表项
        var uninstallPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (var basePath in uninstallPaths)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(basePath, true);
                if (key == null) continue;

                foreach (var subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = key.OpenSubKey(subName);
                        var name = sub?.GetValue("DisplayName") as string;
                        var loc = (sub?.GetValue("InstallLocation") as string)?.TrimEnd('\\', '/');
                        if ((name != null && name.Equals(program.Name, StringComparison.OrdinalIgnoreCase)) ||
                            (loc != null && loc.Equals(program.InstallLocation.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)))
                        {
                            key.DeleteSubKeyTree(subName, false);
                            AddLog($"    注册表项已删除: {basePath}\\{subName}");
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // HKCU
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(uninstallPaths[0], true);
            if (key != null)
            {
                foreach (var subName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var sub = key.OpenSubKey(subName);
                        var name = sub?.GetValue("DisplayName") as string;
                        if (name != null && name.Equals(program.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            key.DeleteSubKeyTree(subName, false);
                            AddLog($"    HKCU注册表项已删除: {subName}");
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        // 清理已分析到的注册表引用
        foreach (var r in program.References.Where(r => r.Type == ReferenceType.RegistryValue))
        {
            try
            {
                var parts = r.Location.Split('\\', 2);
                Microsoft.Win32.RegistryKey? root = parts[0] switch
                {
                    "HKEY_LOCAL_MACHINE" => Microsoft.Win32.Registry.LocalMachine,
                    "HKEY_CURRENT_USER" => Microsoft.Win32.Registry.CurrentUser,
                    "HKEY_CLASSES_ROOT" => Microsoft.Win32.Registry.ClassesRoot,
                    _ => null
                };

                if (root != null && parts.Length > 1)
                {
                    using var k = root.OpenSubKey(parts[1], true);
                    if (k != null && !string.IsNullOrEmpty(r.ValueName))
                    {
                        k.DeleteValue(r.ValueName, false);
                    }
                }
            }
            catch { }
        }
    }

    private void CleanShortcuts(InstalledProgram program)
    {
        var searchDirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };

        foreach (var dir in searchDirs)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            try
            {
                foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
                {
                    try
                    {
                        var target = GetShortcutTarget(lnk);
                        if (target != null && target.StartsWith(program.InstallLocation, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(lnk);
                            AddLog($"    快捷方式已删除: {Path.GetFileName(lnk)}");
                        }
                    }
                    catch { }
                }
            }
            catch { }
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
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut);
            System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            return target;
        }
        catch { return null; }
    }

    private void SelectAll()
    {
        foreach (var p in Programs)
            if (p.Risk != RiskLevel.Critical)
                p.IsSelected = true;
        UpdateSelectionStats();
    }

    private void DeselectAll()
    {
        foreach (var p in Programs)
            p.IsSelected = false;
        UpdateSelectionStats();
    }

    private void SelectRecommended()
    {
        foreach (var p in Programs)
            p.IsSelected = p.Recommendation == Recommendation.Recommended;
        UpdateSelectionStats();
    }

    private void SelectCaution()
    {
        foreach (var p in Programs)
            p.IsSelected = p.Recommendation == Recommendation.Caution;
        UpdateSelectionStats();
    }

    private void SelectNotRecommended()
    {
        foreach (var p in Programs)
            p.IsSelected = p.Recommendation == Recommendation.NotRecommended;
        UpdateSelectionStats();
    }

    #endregion

    #region Helpers

    private void LoadAvailableDrives()
    {
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady && drive.DriveType == DriveType.Fixed && drive.Name != @"C:\")
            {
                AvailableDrives.Add(drive.Name);
            }
        }
        if (AvailableDrives.Count > 0)
            SelectedDrive = AvailableDrives[0];
    }

    private void Program_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(InstalledProgram.IsSelected))
            UpdateSelectionStats();
    }

    private void UpdateSelectionStats()
    {
        var selected = Programs.Where(p => p.IsSelected).ToList();
        SelectedCount = selected.Count;
        SelectedSize = selected.Sum(p => p.SizeBytes);
    }

    private void AddLog(string message)
    {
        try
        {
            if (Application.Current?.Dispatcher.CheckAccess() == true)
            {
                LogMessages.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                if (LogMessages.Count > 2000) { while (LogMessages.Count > 1800) LogMessages.RemoveAt(0); }
            }
            else
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        LogMessages.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                        while (LogMessages.Count > 2000) LogMessages.RemoveAt(0);
                    }
                    catch { }
                });
            }
        }
        catch { }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    #endregion
}
