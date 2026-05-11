using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CDriveMigrator.Models;

public enum RiskLevel
{
    Safe,
    Low,
    Medium,
    High,
    Critical
}

public enum ReferenceType
{
    RegistryValue,
    Shortcut,
    WindowsService,
    EnvironmentVariable,
    ScheduledTask,
    ConfigFile
}

public enum Recommendation
{
    Recommended,
    Caution,
    NotRecommended
}

public enum MigrationStatus
{
    Pending,
    Scanning,
    Analyzing,
    Moving,
    FixingReferences,
    CreatingJunction,
    Verifying,
    Completed,
    Failed,
    RolledBack
}

public class InstalledProgram : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isAnalyzed;
    private MigrationStatus _status = MigrationStatus.Pending;
    private RiskLevel _risk = RiskLevel.Safe;
    private string _riskReason = "";
    private Recommendation _recommendation = Recommendation.Caution;
    private string _recommendReason = "";

    public string Name { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public long SizeBytes { get; set; }
    public string UninstallString { get; set; } = "";
    public string RegistryKeyPath { get; set; } = "";
    public bool IsSystemComponent { get; set; }
    public ObservableCollection<ProgramReference> References { get; set; } = new();

    public string SizeDisplay => FormatSize(SizeBytes);

    public int ReferenceCount => References.Count;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public bool IsAnalyzed
    {
        get => _isAnalyzed;
        set { _isAnalyzed = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReferenceCount)); }
    }

    public MigrationStatus Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusDisplay)); }
    }

    public RiskLevel Risk
    {
        get => _risk;
        set { _risk = value; OnPropertyChanged(); OnPropertyChanged(nameof(RiskDisplay)); }
    }

    public string RiskReason
    {
        get => _riskReason;
        set { _riskReason = value; OnPropertyChanged(); }
    }

    public Recommendation Recommendation
    {
        get => _recommendation;
        set { _recommendation = value; OnPropertyChanged(); OnPropertyChanged(nameof(RecommendDisplay)); }
    }

    public string RecommendReason
    {
        get => _recommendReason;
        set { _recommendReason = value; OnPropertyChanged(); }
    }

    public string RecommendDisplay => Recommendation switch
    {
        Recommendation.Recommended => "建议迁移",
        Recommendation.Caution => "需分析",
        Recommendation.NotRecommended => "不建议",
        _ => ""
    };

    public string StatusDisplay => Status switch
    {
        MigrationStatus.Pending => "待处理",
        MigrationStatus.Scanning => "扫描中...",
        MigrationStatus.Analyzing => "分析中...",
        MigrationStatus.Moving => "迁移中...",
        MigrationStatus.FixingReferences => "修复引用...",
        MigrationStatus.CreatingJunction => "创建联接...",
        MigrationStatus.Verifying => "验证中...",
        MigrationStatus.Completed => "已完成 ✓",
        MigrationStatus.Failed => "失败 ✗",
        MigrationStatus.RolledBack => "已回滚",
        _ => ""
    };

    public string RiskDisplay => Risk switch
    {
        RiskLevel.Safe => "安全",
        RiskLevel.Low => "低风险",
        RiskLevel.Medium => "中风险",
        RiskLevel.High => "高风险",
        RiskLevel.Critical => "危险",
        _ => ""
    };

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "未知";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:F1} {units[unit]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class ProgramReference
{
    public ReferenceType Type { get; set; }
    public string Location { get; set; } = "";
    public string ValueName { get; set; } = "";
    public string OriginalValue { get; set; } = "";
    public string NewValue { get; set; } = "";
    public bool IsFixed { get; set; }
    public string Description { get; set; } = "";

    public string TypeDisplay => Type switch
    {
        ReferenceType.RegistryValue => "注册表",
        ReferenceType.Shortcut => "快捷方式",
        ReferenceType.WindowsService => "Windows服务",
        ReferenceType.EnvironmentVariable => "环境变量",
        ReferenceType.ScheduledTask => "计划任务",
        ReferenceType.ConfigFile => "配置文件",
        _ => Type.ToString()
    };
}

public class MigrationSnapshot
{
    public string ProgramName { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public List<ReferenceBackup> ReferenceBackups { get; set; } = new();
    public List<string> KilledProcesses { get; set; } = new();
    public bool FilesMoved { get; set; }
    public bool JunctionCreated { get; set; }
}

public class ReferenceBackup
{
    public ProgramReference Reference { get; set; } = new();
    public string BackupValue { get; set; } = "";
    public bool WasModified { get; set; }
}

public class MigrationResult
{
    public bool Success { get; set; }
    public string ProgramName { get; set; } = "";
    public string Message { get; set; } = "";
    public long SpaceSaved { get; set; }
    public List<string> FixedReferences { get; set; } = new();
    public List<string> FailedFixes { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
