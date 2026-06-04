using CDriveMigrator.Models;
using Microsoft.Win32;
using System.IO;

namespace CDriveMigrator.Services;

public class ProgramScanner
{
    private static readonly string[] UninstallKeyPaths =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    private static readonly HashSet<string> SystemDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Windows",
        @"C:\Program Files",
        @"C:\Program Files (x86)",
    };

    // Known safe publishers — standard desktop apps, games, tools
    private static readonly HashSet<string> SafePublishers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Valve", "Valve Corporation", "Steam", "Epic Games",
        "Adobe Systems", "Adobe Systems Incorporated", "Adobe Inc.", "Adobe",
        "JetBrains", "JetBrains s.r.o.",
        "Google LLC", "Google Inc.",
        "Mozilla", "Mozilla Corporation",
        "VideoLAN", "GIMP", "Blender Foundation", "Audacity",
        "7-Zip", "Igor Pavlov",
        "Notepad++", "WinRAR", "Bandisoft",
        "OBS", "OBS Project",
        "Discord Inc.", "Zoom", "Zoom Video Communications, Inc.",
        "Tencent", "腾讯", "网易", "Netease",
        "Riot Games", "Ubisoft", "Electronic Arts",
        "NVIDIA", "NVIDIA Corporation",
    };

    // Publishers that suggest system-level integration
    private static readonly HashSet<string> SystemPublishers = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft Corporation", "Microsoft",
        "Intel", "Intel Corporation", "Intel(R) Corporation",
        "Realtek", "Realtek Semiconductor",
        "Broadcom", "Qualcomm", "Synaptics",
    };

    // Keywords in program name that suggest not safe to migrate
    private static readonly string[] UnsafeNameKeywords =
    [
        "driver", "驱动", "runtime", "redistributable", "redist",
        ".NET", "Visual C++", "MSVC", "DirectX",
        "SDK", "Framework",
        "antivirus", "security", "defender", "firewall",
        "杀毒", "安全", "防火墙",
        "update", "updater",
    ];

    // Keywords in program name that suggest safe to migrate
    private static readonly string[] SafeNameKeywords =
    [
        "game", "games", "游戏",
        "editor", "编辑", "studio",
        "player", "播放",
        "browser", "浏览器",
        "chat", "聊天", "通讯",
    ];

    public async Task<List<InstalledProgram>> ScanAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var programs = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await Task.Run(() =>
        {
            foreach (var keyPath in UninstallKeyPaths)
            {
                ct.ThrowIfCancellationRequested();
                ScanRegistryHive(Registry.LocalMachine, keyPath, programs, seen, progress, ct);
            }
            ScanRegistryHive(Registry.CurrentUser, UninstallKeyPaths[0], programs, seen, progress, ct);

            progress?.Report("正在计算文件夹大小并评估迁移安全性...");
            foreach (var program in programs)
            {
                ct.ThrowIfCancellationRequested();
                if (Directory.Exists(program.InstallLocation))
                {
                    progress?.Report($"评估: {program.Name}");
                    program.SizeBytes = GetDirectorySize(program.InstallLocation);
                }
                AssessRecommendation(program);
            }
        }, ct);

        programs.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        return programs;
    }

    /// <summary>
    /// 扫描后自动预评估：根据安装路径、发布者、名称关键词判断迁移安全性
    /// </summary>
    private static void AssessRecommendation(InstalledProgram p)
    {
        var reasons = new List<string>();
        int score = 50; // 0=危险, 50=中性, 100=安全

        // ── 路径分析 ──
        var path = p.InstallLocation;

        if (path.StartsWith(@"C:\Program Files\", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(@"C:\Program Files (x86)\", StringComparison.OrdinalIgnoreCase))
        {
            // Standard install location — generally safe
            score += 15;
            reasons.Add("标准安装目录");
        }

        if (path.StartsWith(@"C:\Users\", StringComparison.OrdinalIgnoreCase))
        {
            // User-local app — very safe
            score += 25;
            reasons.Add("用户目录安装，迁移安全");
        }

        if (path.StartsWith(@"C:\ProgramData\", StringComparison.OrdinalIgnoreCase))
        {
            score -= 15;
            reasons.Add("ProgramData目录，可能被系统服务引用");
        }

        // Check if it's a shallow path like C:\SomeApp (non-standard)
        var parts = path.TrimStart('C', 'c', ':', '\\').Split('\\');
        if (parts.Length == 1)
        {
            score += 5;
            reasons.Add("根目录独立文件夹");
        }

        // ── 发布者分析 ──
        if (!string.IsNullOrEmpty(p.Publisher))
        {
            if (SafePublishers.Contains(p.Publisher))
            {
                score += 25;
                reasons.Add($"已知安全发布者: {p.Publisher}");
            }
            else if (SystemPublishers.Any(sp =>
                p.Publisher.Contains(sp, StringComparison.OrdinalIgnoreCase)))
            {
                score -= 20;
                reasons.Add($"系统级发布者: {p.Publisher}，可能与系统深度集成");
            }
        }

        // ── 名称关键词分析 ──
        var nameLower = p.Name.ToLowerInvariant();
        foreach (var kw in UnsafeNameKeywords)
        {
            if (nameLower.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                score -= 30;
                reasons.Add($"名称含系统组件关键词「{kw}」");
                break;
            }
        }

        foreach (var kw in SafeNameKeywords)
        {
            if (nameLower.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                score += 15;
                reasons.Add($"名称为常见应用类型「{kw}」");
                break;
            }
        }

        // ── 大小分析 ──
        if (p.SizeBytes > 500 * 1024 * 1024) // > 500MB
        {
            score += 10;
            reasons.Add($"占用较大 ({InstalledProgram.FormatSize(p.SizeBytes)})，迁移收益高");
        }
        else if (p.SizeBytes > 0 && p.SizeBytes < 5 * 1024 * 1024) // < 5MB
        {
            score -= 10;
            reasons.Add("体积很小，迁移收益低");
        }

        // ── 卸载字符串分析 ──
        if (!string.IsNullOrEmpty(p.UninstallString))
        {
            var uninstLower = p.UninstallString.ToLowerInvariant();
            if (uninstLower.Contains("msiexec"))
            {
                score += 5;
                reasons.Add("MSI安装包，结构规范");
            }
        }

        // ── 是否有服务 (快速检测：检查注册表中有无 service 相关) ──
        if (!string.IsNullOrEmpty(p.UninstallString) &&
            p.UninstallString.Contains("service", StringComparison.OrdinalIgnoreCase))
        {
            score -= 10;
            reasons.Add("可能关联Windows服务");
        }

        // ── 最终决策 ──
        score = Math.Clamp(score, 0, 100);

        if (score >= 65)
        {
            p.Recommendation = Recommendation.Recommended;
            p.Risk = RiskLevel.Safe;
        }
        else if (score >= 35)
        {
            p.Recommendation = Recommendation.Caution;
            p.Risk = RiskLevel.Medium;
        }
        else
        {
            p.Recommendation = Recommendation.NotRecommended;
            p.Risk = RiskLevel.High;
        }

        p.RecommendReason = string.Join("；", reasons);
        p.RiskReason = p.RecommendReason;
    }

    private static void ScanRegistryHive(RegistryKey rootKey, string keyPath,
        List<InstalledProgram> programs, HashSet<string> seen,
        IProgress<string>? progress, CancellationToken ct)
    {
        using var key = rootKey.OpenSubKey(keyPath);
        if (key == null) return;

        foreach (var subKeyName in key.GetSubKeyNames())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var subKey = key.OpenSubKey(subKeyName);
                if (subKey == null) continue;

                var name = subKey.GetValue("DisplayName") as string;
                var installLocation = (subKey.GetValue("InstallLocation") as string)?.TrimEnd('\\', '/');

                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installLocation))
                    continue;

                if (!installLocation.StartsWith(@"C:\", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (IsSystemRoot(installLocation))
                    continue;

                if (!seen.Add(installLocation))
                    continue;

                var sysComp = subKey.GetValue("SystemComponent");
                var isSystem = sysComp is int sci && sci == 1;
                if (isSystem) continue;

                progress?.Report($"发现: {name}");

                programs.Add(new InstalledProgram
                {
                    Name = name,
                    InstallLocation = installLocation,
                    Publisher = subKey.GetValue("Publisher") as string ?? "",
                    Version = subKey.GetValue("DisplayVersion") as string ?? "",
                    SizeBytes = ((subKey.GetValue("EstimatedSize") as int?) ?? 0) * 1024L,
                    UninstallString = subKey.GetValue("UninstallString") as string ?? "",
                    RegistryKeyPath = subKey.Name,
                    IsSystemComponent = false
                });
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取注册表项 {subKeyName} 失败: {ex.Message}");
            }
        }
    }

    private static bool IsSystemRoot(string path)
    {
        var normalized = path.TrimEnd('\\');
        if (SystemDirs.Contains(normalized)) return true;
        if (normalized.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static long GetDirectorySize(string path)
    {
        long size = 0;
        try
        {
            foreach (var fi in new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            }))
            {
                try { size += fi.Length; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"计算目录大小失败 {path}: {ex.Message}");
        }
        return size;
    }
}
