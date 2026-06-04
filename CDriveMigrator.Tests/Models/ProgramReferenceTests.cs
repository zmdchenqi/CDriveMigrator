using CDriveMigrator.Models;

namespace CDriveMigrator.Tests.Models;

public class ProgramReferenceTests
{
    [Theory]
    [InlineData(ReferenceType.RegistryValue, "注册表")]
    [InlineData(ReferenceType.Shortcut, "快捷方式")]
    [InlineData(ReferenceType.WindowsService, "Windows服务")]
    [InlineData(ReferenceType.EnvironmentVariable, "环境变量")]
    [InlineData(ReferenceType.ScheduledTask, "计划任务")]
    [InlineData(ReferenceType.ConfigFile, "配置文件")]
    public void TypeDisplay_ReturnsCorrectText(ReferenceType type, string expected)
    {
        var reference = new ProgramReference { Type = type };
        Assert.Equal(expected, reference.TypeDisplay);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var reference = new ProgramReference();

        Assert.Equal("", reference.Location);
        Assert.Equal("", reference.ValueName);
        Assert.Equal("", reference.OriginalValue);
        Assert.Equal("", reference.NewValue);
        Assert.False(reference.IsFixed);
        Assert.Equal("", reference.Description);
    }
}
