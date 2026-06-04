using CDriveMigrator.Models;

namespace CDriveMigrator.Tests.Models;

public class MigrationResultTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var result = new MigrationResult();

        Assert.False(result.Success);
        Assert.Equal("", result.ProgramName);
        Assert.Equal("", result.Message);
        Assert.Equal(0, result.SpaceSaved);
        Assert.NotNull(result.FixedReferences);
        Assert.Empty(result.FixedReferences);
        Assert.NotNull(result.FailedFixes);
        Assert.Empty(result.FailedFixes);
        Assert.NotNull(result.Warnings);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var result = new MigrationResult
        {
            Success = true,
            ProgramName = "TestApp",
            Message = "Migration completed",
            SpaceSaved = 1024 * 1024,
            FixedReferences = new List<string> { "reg1", "reg2" },
            FailedFixes = new List<string> { "fix1" },
            Warnings = new List<string> { "warn1" }
        };

        Assert.True(result.Success);
        Assert.Equal("TestApp", result.ProgramName);
        Assert.Equal("Migration completed", result.Message);
        Assert.Equal(1024 * 1024, result.SpaceSaved);
        Assert.Equal(2, result.FixedReferences.Count);
        Assert.Single(result.FailedFixes);
        Assert.Single(result.Warnings);
    }
}

public class MigrationSnapshotTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var snapshot = new MigrationSnapshot();

        Assert.Equal("", snapshot.ProgramName);
        Assert.Equal("", snapshot.SourcePath);
        Assert.Equal("", snapshot.TargetPath);
        Assert.NotNull(snapshot.ReferenceBackups);
        Assert.Empty(snapshot.ReferenceBackups);
        Assert.NotNull(snapshot.KilledProcesses);
        Assert.Empty(snapshot.KilledProcesses);
        Assert.False(snapshot.FilesMoved);
        Assert.False(snapshot.JunctionCreated);
    }
}
