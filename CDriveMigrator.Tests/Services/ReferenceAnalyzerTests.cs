using CDriveMigrator.Models;
using CDriveMigrator.Services;

namespace CDriveMigrator.Tests.Services;

public class ReferenceAnalyzerTests
{
    #region ContainsPath Tests

    [Fact]
    public void ContainsPath_DirectMatch_ReturnsTrue()
    {
        Assert.True(ReferenceAnalyzer.ContainsPath(
            @"C:\Program Files\MyApp\bin\app.exe",
            @"C:\Program Files\MyApp"));
    }

    [Fact]
    public void ContainsPath_CaseInsensitive_ReturnsTrue()
    {
        Assert.True(ReferenceAnalyzer.ContainsPath(
            @"c:\program files\myapp\bin\app.exe",
            @"C:\Program Files\MyApp"));
    }

    [Fact]
    public void ContainsPath_TrailingBackslash_ReturnsTrue()
    {
        Assert.True(ReferenceAnalyzer.ContainsPath(
            @"C:\Program Files\MyApp\something",
            @"C:\Program Files\MyApp"));
    }

    [Fact]
    public void ContainsPath_ForwardSlashes_ReturnsTrue()
    {
        Assert.True(ReferenceAnalyzer.ContainsPath(
            "C:/Program Files/MyApp/bin/app.exe",
            @"C:\Program Files\MyApp"));
    }

    [Fact]
    public void ContainsPath_QuotedPath_ReturnsTrue()
    {
        Assert.True(ReferenceAnalyzer.ContainsPath(
            "\"C:\\Program Files\\MyApp\" --arg",
            @"C:\Program Files\MyApp"));
    }

    [Fact]
    public void ContainsPath_NoMatch_ReturnsFalse()
    {
        Assert.False(ReferenceAnalyzer.ContainsPath(
            @"C:\Program Files\OtherApp\bin\app.exe",
            @"C:\Program Files\MyApp"));
    }

    [Fact]
    public void ContainsPath_EmptyText_ReturnsFalse()
    {
        Assert.False(ReferenceAnalyzer.ContainsPath("", @"C:\Program Files\MyApp"));
    }

    [Fact]
    public void ContainsPath_EmptyPath_ReturnsFalse()
    {
        Assert.False(ReferenceAnalyzer.ContainsPath(@"C:\Program Files\MyApp", ""));
    }

    [Fact]
    public void ContainsPath_NullText_ReturnsFalse()
    {
        Assert.False(ReferenceAnalyzer.ContainsPath(null!, @"C:\Path"));
    }

    [Fact]
    public void ContainsPath_NullPath_ReturnsFalse()
    {
        Assert.False(ReferenceAnalyzer.ContainsPath("some text", null!));
    }

    #endregion

    #region ParseCsvLine Tests

    [Fact]
    public void ParseCsvLine_SimpleFields_ParsesCorrectly()
    {
        var result = ReferenceAnalyzer.ParseCsvLine("field1,field2,field3");

        Assert.Equal(3, result.Count);
        Assert.Equal("field1", result[0]);
        Assert.Equal("field2", result[1]);
        Assert.Equal("field3", result[2]);
    }

    [Fact]
    public void ParseCsvLine_QuotedFields_ParsesCorrectly()
    {
        var result = ReferenceAnalyzer.ParseCsvLine("\"field,with,commas\",field2,field3");

        Assert.Equal(3, result.Count);
        Assert.Equal("field,with,commas", result[0]);
        Assert.Equal("field2", result[1]);
        Assert.Equal("field3", result[2]);
    }

    [Fact]
    public void ParseCsvLine_EmptyFields_ParsesCorrectly()
    {
        var result = ReferenceAnalyzer.ParseCsvLine(",,");

        Assert.Equal(3, result.Count);
        Assert.All(result, f => Assert.Equal("", f));
    }

    [Fact]
    public void ParseCsvLine_SingleField_ReturnsSingleElement()
    {
        var result = ReferenceAnalyzer.ParseCsvLine("onlyfield");

        Assert.Single(result);
        Assert.Equal("onlyfield", result[0]);
    }

    [Fact]
    public void ParseCsvLine_EmptyString_ReturnsSingleEmptyField()
    {
        var result = ReferenceAnalyzer.ParseCsvLine("");

        Assert.Single(result);
        Assert.Equal("", result[0]);
    }

    [Fact]
    public void ParseCsvLine_QuotedFieldWithPath_ParsesCorrectly()
    {
        var result = ReferenceAnalyzer.ParseCsvLine(@"""C:\Program Files\App"",Running,Normal");

        Assert.Equal(3, result.Count);
        Assert.Equal(@"C:\Program Files\App", result[0]);
        Assert.Equal("Running", result[1]);
        Assert.Equal("Normal", result[2]);
    }

    #endregion

    #region SplitRegistryPath Tests

    [Fact]
    public void SplitRegistryPath_HKLM_ParsesCorrectly()
    {
        var result = ReferenceAnalyzer.SplitRegistryPath(@"HKEY_LOCAL_MACHINE\SOFTWARE\MyApp");

        Assert.NotNull(result);
        Assert.Equal(@"SOFTWARE\MyApp", result.Value.subKey);
    }

    [Fact]
    public void SplitRegistryPath_HKCU_ParsesCorrectly()
    {
        var result = ReferenceAnalyzer.SplitRegistryPath(@"HKEY_CURRENT_USER\SOFTWARE\MyApp");

        Assert.NotNull(result);
        Assert.Equal(@"SOFTWARE\MyApp", result.Value.subKey);
    }

    [Fact]
    public void SplitRegistryPath_HKCR_ParsesCorrectly()
    {
        var result = ReferenceAnalyzer.SplitRegistryPath(@"HKEY_CLASSES_ROOT\.txt");

        Assert.NotNull(result);
        Assert.Equal(@".txt", result.Value.subKey);
    }

    [Fact]
    public void SplitRegistryPath_Unknown_ReturnsNull()
    {
        var result = ReferenceAnalyzer.SplitRegistryPath(@"HKEY_USERS\SomeKey");

        Assert.Null(result);
    }

    [Fact]
    public void SplitRegistryPath_CaseInsensitive()
    {
        var result = ReferenceAnalyzer.SplitRegistryPath(@"hkey_local_machine\SOFTWARE\Test");

        Assert.NotNull(result);
        Assert.Equal(@"SOFTWARE\Test", result.Value.subKey);
    }

    #endregion

    #region AssessRisk Tests

    [Fact]
    public void AssessRisk_WindowsPath_Critical()
    {
        var program = new InstalledProgram
        {
            Name = "Some App",
            InstallLocation = @"C:\Windows\SomeApp"
        };

        ReferenceAnalyzer.AssessRisk(program);

        Assert.Equal(RiskLevel.Critical, program.Risk);
        Assert.Contains("系统核心组件", program.RiskReason);
    }

    [Fact]
    public void AssessRisk_VisualCppRedistributable_Critical()
    {
        var program = new InstalledProgram
        {
            Name = "Visual C++ Redistributable 2019",
            InstallLocation = @"C:\Program Files\VC"
        };

        ReferenceAnalyzer.AssessRisk(program);

        Assert.Equal(RiskLevel.Critical, program.Risk);
    }

    [Fact]
    public void AssessRisk_DotNetFramework_Critical()
    {
        var program = new InstalledProgram
        {
            Name = ".NET Framework 4.8",
            InstallLocation = @"C:\Program Files\dotnet"
        };

        ReferenceAnalyzer.AssessRisk(program);

        Assert.Equal(RiskLevel.Critical, program.Risk);
    }

    [Fact]
    public void AssessRisk_WithServicesAndScheduledTasks_High()
    {
        var program = new InstalledProgram
        {
            Name = "Complex App",
            InstallLocation = @"C:\Program Files\ComplexApp"
        };
        program.References.Add(new ProgramReference { Type = ReferenceType.WindowsService });
        program.References.Add(new ProgramReference { Type = ReferenceType.ScheduledTask });

        ReferenceAnalyzer.AssessRisk(program);

        Assert.Equal(RiskLevel.High, program.Risk);
        Assert.Contains("Windows 服务和计划任务", program.RiskReason);
    }

    [Fact]
    public void AssessRisk_WithServicesOnly_Medium()
    {
        var program = new InstalledProgram
        {
            Name = "Service App",
            InstallLocation = @"C:\Program Files\ServiceApp"
        };
        program.References.Add(new ProgramReference { Type = ReferenceType.WindowsService });

        ReferenceAnalyzer.AssessRisk(program);

        Assert.Equal(RiskLevel.Medium, program.Risk);
        Assert.Contains("Windows 服务", program.RiskReason);
    }

    [Fact]
    public void AssessRisk_ManyRegistryRefs_Medium()
    {
        var program = new InstalledProgram
        {
            Name = "Heavy Registry App",
            InstallLocation = @"C:\Program Files\HeavyApp"
        };
        for (int i = 0; i < 55; i++)
            program.References.Add(new ProgramReference { Type = ReferenceType.RegistryValue });

        ReferenceAnalyzer.AssessRisk(program);

        Assert.Equal(RiskLevel.Medium, program.Risk);
        Assert.Contains("注册表引用较多", program.RiskReason);
    }

    [Fact]
    public void AssessRisk_ScheduledTasksOnly_Low()
    {
        var program = new InstalledProgram
        {
            Name = "Task App",
            InstallLocation = @"C:\Program Files\TaskApp"
        };
        program.References.Add(new ProgramReference { Type = ReferenceType.ScheduledTask });

        ReferenceAnalyzer.AssessRisk(program);

        Assert.Equal(RiskLevel.Low, program.Risk);
        Assert.Contains("计划任务", program.RiskReason);
    }

    [Fact]
    public void AssessRisk_FewRegistryRefs_Low()
    {
        var program = new InstalledProgram
        {
            Name = "Normal App",
            InstallLocation = @"C:\Program Files\NormalApp"
        };
        for (int i = 0; i < 15; i++)
            program.References.Add(new ProgramReference { Type = ReferenceType.RegistryValue });

        ReferenceAnalyzer.AssessRisk(program);

        Assert.Equal(RiskLevel.Low, program.Risk);
    }

    [Fact]
    public void AssessRisk_SimpleApp_Safe()
    {
        var program = new InstalledProgram
        {
            Name = "Simple App",
            InstallLocation = @"C:\Program Files\SimpleApp"
        };
        program.References.Add(new ProgramReference { Type = ReferenceType.Shortcut });
        program.References.Add(new ProgramReference { Type = ReferenceType.RegistryValue });

        ReferenceAnalyzer.AssessRisk(program);

        Assert.Equal(RiskLevel.Safe, program.Risk);
        Assert.Contains("引用简单", program.RiskReason);
    }

    #endregion

    #region ReassessRecommendation Tests

    [Fact]
    public void ReassessRecommendation_Critical_NotRecommended()
    {
        var program = new InstalledProgram
        {
            Risk = RiskLevel.Critical,
            RiskReason = "系统核心组件"
        };

        ReferenceAnalyzer.ReassessRecommendation(program);

        Assert.Equal(Recommendation.NotRecommended, program.Recommendation);
        Assert.Contains("⛔", program.RecommendReason);
    }

    [Fact]
    public void ReassessRecommendation_High_NotRecommended()
    {
        var program = new InstalledProgram
        {
            Risk = RiskLevel.High,
            RiskReason = "高风险原因"
        };

        ReferenceAnalyzer.ReassessRecommendation(program);

        Assert.Equal(Recommendation.NotRecommended, program.Recommendation);
        Assert.Contains("⚠ 高风险", program.RecommendReason);
    }

    [Fact]
    public void ReassessRecommendation_MediumWithService_Caution()
    {
        var program = new InstalledProgram
        {
            Risk = RiskLevel.Medium,
            RiskReason = "含服务"
        };
        program.References.Add(new ProgramReference { Type = ReferenceType.WindowsService });

        ReferenceAnalyzer.ReassessRecommendation(program);

        Assert.Equal(Recommendation.Caution, program.Recommendation);
        Assert.Contains("含服务引用", program.RecommendReason);
    }

    [Fact]
    public void ReassessRecommendation_MediumWithoutService_Recommended()
    {
        var program = new InstalledProgram
        {
            Risk = RiskLevel.Medium,
            RiskReason = "注册表引用较多"
        };
        for (int i = 0; i < 55; i++)
            program.References.Add(new ProgramReference { Type = ReferenceType.RegistryValue });

        ReferenceAnalyzer.ReassessRecommendation(program);

        Assert.Equal(Recommendation.Recommended, program.Recommendation);
        Assert.Contains("可迁移", program.RecommendReason);
    }

    [Fact]
    public void ReassessRecommendation_Low_Recommended()
    {
        var program = new InstalledProgram
        {
            Risk = RiskLevel.Low,
            RiskReason = "包含计划任务引用"
        };

        ReferenceAnalyzer.ReassessRecommendation(program);

        Assert.Equal(Recommendation.Recommended, program.Recommendation);
        Assert.Contains("✓ 安全迁移", program.RecommendReason);
    }

    [Fact]
    public void ReassessRecommendation_Safe_Recommended()
    {
        var program = new InstalledProgram
        {
            Risk = RiskLevel.Safe,
            RiskReason = "引用简单"
        };

        ReferenceAnalyzer.ReassessRecommendation(program);

        Assert.Equal(Recommendation.Recommended, program.Recommendation);
        Assert.Contains("✓ 安全迁移", program.RecommendReason);
    }

    #endregion
}
