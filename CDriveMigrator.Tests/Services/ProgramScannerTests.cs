using CDriveMigrator.Models;
using CDriveMigrator.Services;

namespace CDriveMigrator.Tests.Services;

public class ProgramScannerTests
{
    #region IsSystemRoot Tests

    [Theory]
    [InlineData(@"C:\Windows", true)]
    [InlineData(@"C:\Windows\", true)]
    [InlineData(@"C:\Windows\System32", true)]
    [InlineData(@"C:\Windows\SysWOW64\something", true)]
    [InlineData(@"C:\Program Files", true)]
    [InlineData(@"C:\Program Files\", true)]
    [InlineData(@"C:\Program Files (x86)", true)]
    [InlineData(@"C:\Program Files (x86)\", true)]
    public void IsSystemRoot_ReturnsTrue_ForSystemPaths(string path, bool expected)
    {
        Assert.Equal(expected, ProgramScanner.IsSystemRoot(path));
    }

    [Theory]
    [InlineData(@"C:\Program Files\SomeApp")]
    [InlineData(@"C:\Users\TestUser\AppData")]
    [InlineData(@"D:\Games")]
    [InlineData(@"C:\MyApp")]
    [InlineData(@"C:\ProgramData\SomeApp")]
    public void IsSystemRoot_ReturnsFalse_ForNonSystemPaths(string path)
    {
        Assert.False(ProgramScanner.IsSystemRoot(path));
    }

    #endregion

    #region AssessRecommendation Tests

    [Fact]
    public void AssessRecommendation_SafePublisher_ProgramFiles_Recommended()
    {
        var program = new InstalledProgram
        {
            Name = "Steam",
            InstallLocation = @"C:\Program Files\Steam",
            Publisher = "Valve Corporation",
            SizeBytes = 600 * 1024 * 1024 // 600MB
        };

        ProgramScanner.AssessRecommendation(program);

        Assert.Equal(Recommendation.Recommended, program.Recommendation);
        Assert.Equal(RiskLevel.Safe, program.Risk);
        Assert.Contains("已知安全发布者", program.RecommendReason);
    }

    [Fact]
    public void AssessRecommendation_SystemPublisher_NotRecommended()
    {
        var program = new InstalledProgram
        {
            Name = "Intel Driver Update",
            InstallLocation = @"C:\ProgramData\IntelDriver",
            Publisher = "Intel Corporation",
            SizeBytes = 2 * 1024 * 1024 // 2MB - small
        };

        ProgramScanner.AssessRecommendation(program);

        // score: 50 - 15 (ProgramData) - 20 (SystemPublisher) - 30 (driver keyword) - 10 (small) = -25 -> clamped to 0
        Assert.Equal(Recommendation.NotRecommended, program.Recommendation);
        Assert.Equal(RiskLevel.High, program.Risk);
    }

    [Fact]
    public void AssessRecommendation_UserDirectory_Safe()
    {
        var program = new InstalledProgram
        {
            Name = "My Chat App",
            InstallLocation = @"C:\Users\TestUser\AppData\Local\ChatApp",
            Publisher = "SomePublisher",
            SizeBytes = 100 * 1024 * 1024 // 100MB
        };

        ProgramScanner.AssessRecommendation(program);

        // score: 50 + 25 (Users) + 15 (chat keyword) = 90
        Assert.Equal(Recommendation.Recommended, program.Recommendation);
        Assert.Equal(RiskLevel.Safe, program.Risk);
    }

    [Fact]
    public void AssessRecommendation_UnsafeNameKeyword_LowersScore()
    {
        var program = new InstalledProgram
        {
            Name = "Visual C++ Redistributable",
            InstallLocation = @"C:\Program Files\VC",
            Publisher = "",
            SizeBytes = 50 * 1024 * 1024
        };

        ProgramScanner.AssessRecommendation(program);

        // score: 50 + 15 (ProgramFiles) - 30 (redistributable keyword) = 35 -> Caution
        Assert.Equal(Recommendation.Caution, program.Recommendation);
        Assert.Equal(RiskLevel.Medium, program.Risk);
    }

    [Fact]
    public void AssessRecommendation_GameName_IncreasesScore()
    {
        var program = new InstalledProgram
        {
            Name = "Epic Games Launcher",
            InstallLocation = @"C:\Program Files\EpicGames",
            Publisher = "Epic Games",
            SizeBytes = 800 * 1024 * 1024 // 800MB
        };

        ProgramScanner.AssessRecommendation(program);

        // score: 50 + 15 (ProgramFiles) + 25 (SafePublisher) + 15 (games keyword) + 10 (large) = 115 -> clamped to 100
        Assert.Equal(Recommendation.Recommended, program.Recommendation);
        Assert.Equal(RiskLevel.Safe, program.Risk);
    }

    [Fact]
    public void AssessRecommendation_MsiInstaller_BonusPoints()
    {
        var program = new InstalledProgram
        {
            Name = "SomeApp",
            InstallLocation = @"C:\Program Files\SomeApp",
            Publisher = "",
            SizeBytes = 50 * 1024 * 1024,
            UninstallString = "MsiExec.exe /I{GUID}"
        };

        ProgramScanner.AssessRecommendation(program);

        // score: 50 + 15 (ProgramFiles) + 5 (msiexec) = 70
        Assert.Equal(Recommendation.Recommended, program.Recommendation);
    }

    [Fact]
    public void AssessRecommendation_ServiceRelated_ReducesScore()
    {
        var program = new InstalledProgram
        {
            Name = "SomeApp",
            InstallLocation = @"C:\Program Files\SomeApp",
            Publisher = "",
            SizeBytes = 50 * 1024 * 1024,
            UninstallString = "uninstall.exe --remove-service"
        };

        ProgramScanner.AssessRecommendation(program);

        // score: 50 + 15 (ProgramFiles) - 10 (service) = 55 -> Caution
        Assert.Equal(Recommendation.Caution, program.Recommendation);
    }

    [Fact]
    public void AssessRecommendation_VerySmallApp_ReducesScore()
    {
        var program = new InstalledProgram
        {
            Name = "TinyUtil",
            InstallLocation = @"C:\TinyUtil",
            Publisher = "",
            SizeBytes = 1 * 1024 * 1024 // 1MB
        };

        ProgramScanner.AssessRecommendation(program);

        // score: 50 + 5 (root dir) - 10 (small) = 45 -> Caution
        Assert.Equal(Recommendation.Caution, program.Recommendation);
    }

    #endregion
}
