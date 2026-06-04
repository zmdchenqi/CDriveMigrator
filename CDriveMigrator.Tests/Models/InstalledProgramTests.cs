using CDriveMigrator.Models;
using System.ComponentModel;

namespace CDriveMigrator.Tests.Models;

public class InstalledProgramTests
{
    [Theory]
    [InlineData(0, "未知")]
    [InlineData(-1, "未知")]
    [InlineData(512, "512.0 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(1099511627776, "1.0 TB")]
    public void FormatSize_ReturnsExpectedString(long bytes, string expected)
    {
        var result = InstalledProgram.FormatSize(bytes);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(MigrationStatus.Pending, "待处理")]
    [InlineData(MigrationStatus.Scanning, "扫描中...")]
    [InlineData(MigrationStatus.Analyzing, "分析中...")]
    [InlineData(MigrationStatus.Moving, "迁移中...")]
    [InlineData(MigrationStatus.FixingReferences, "修复引用...")]
    [InlineData(MigrationStatus.CreatingJunction, "创建联接...")]
    [InlineData(MigrationStatus.Verifying, "验证中...")]
    [InlineData(MigrationStatus.Completed, "已完成 ✓")]
    [InlineData(MigrationStatus.Failed, "失败 ✗")]
    [InlineData(MigrationStatus.RolledBack, "已回滚")]
    public void StatusDisplay_ReturnsCorrectText(MigrationStatus status, string expected)
    {
        var program = new InstalledProgram { Status = status };
        Assert.Equal(expected, program.StatusDisplay);
    }

    [Theory]
    [InlineData(RiskLevel.Safe, "安全")]
    [InlineData(RiskLevel.Low, "低风险")]
    [InlineData(RiskLevel.Medium, "中风险")]
    [InlineData(RiskLevel.High, "高风险")]
    [InlineData(RiskLevel.Critical, "危险")]
    public void RiskDisplay_ReturnsCorrectText(RiskLevel risk, string expected)
    {
        var program = new InstalledProgram { Risk = risk };
        Assert.Equal(expected, program.RiskDisplay);
    }

    [Theory]
    [InlineData(Recommendation.Recommended, "建议迁移")]
    [InlineData(Recommendation.Caution, "需分析")]
    [InlineData(Recommendation.NotRecommended, "不建议")]
    public void RecommendDisplay_ReturnsCorrectText(Recommendation rec, string expected)
    {
        var program = new InstalledProgram { Recommendation = rec };
        Assert.Equal(expected, program.RecommendDisplay);
    }

    [Fact]
    public void IsSelected_RaisesPropertyChanged()
    {
        var program = new InstalledProgram();
        var raised = new List<string>();
        program.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        program.IsSelected = true;

        Assert.Contains("IsSelected", raised);
        Assert.True(program.IsSelected);
    }

    [Fact]
    public void IsAnalyzed_RaisesPropertyChangedForReferenceCount()
    {
        var program = new InstalledProgram();
        var raised = new List<string>();
        program.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        program.IsAnalyzed = true;

        Assert.Contains("IsAnalyzed", raised);
        Assert.Contains("ReferenceCount", raised);
    }

    [Fact]
    public void Status_RaisesPropertyChangedForStatusDisplay()
    {
        var program = new InstalledProgram();
        var raised = new List<string>();
        program.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        program.Status = MigrationStatus.Completed;

        Assert.Contains("Status", raised);
        Assert.Contains("StatusDisplay", raised);
    }

    [Fact]
    public void Risk_RaisesPropertyChangedForRiskDisplay()
    {
        var program = new InstalledProgram();
        var raised = new List<string>();
        program.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        program.Risk = RiskLevel.High;

        Assert.Contains("Risk", raised);
        Assert.Contains("RiskDisplay", raised);
    }

    [Fact]
    public void Recommendation_RaisesPropertyChangedForRecommendDisplay()
    {
        var program = new InstalledProgram();
        var raised = new List<string>();
        program.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        program.Recommendation = Recommendation.Recommended;

        Assert.Contains("Recommendation", raised);
        Assert.Contains("RecommendDisplay", raised);
    }

    [Fact]
    public void ReferenceCount_ReturnsCollectionCount()
    {
        var program = new InstalledProgram();
        Assert.Equal(0, program.ReferenceCount);

        program.References.Add(new ProgramReference { Type = ReferenceType.RegistryValue });
        program.References.Add(new ProgramReference { Type = ReferenceType.Shortcut });

        Assert.Equal(2, program.ReferenceCount);
    }

    [Fact]
    public void SizeDisplay_UsesFormatSize()
    {
        var program = new InstalledProgram { SizeBytes = 1048576 };
        Assert.Equal("1.0 MB", program.SizeDisplay);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var program = new InstalledProgram();

        Assert.Equal("", program.Name);
        Assert.Equal("", program.InstallLocation);
        Assert.Equal("", program.Publisher);
        Assert.Equal("", program.Version);
        Assert.Equal(0, program.SizeBytes);
        Assert.False(program.IsSelected);
        Assert.False(program.IsAnalyzed);
        Assert.Equal(MigrationStatus.Pending, program.Status);
        Assert.Equal(RiskLevel.Safe, program.Risk);
        Assert.Equal(Recommendation.Caution, program.Recommendation);
    }
}
