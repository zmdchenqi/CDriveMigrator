using CDriveMigrator.Services;
using System.IO;

namespace CDriveMigrator.Tests.Services;

public class JunctionManagerTests
{
    [Fact]
    public void IsJunction_NonExistentPath_ReturnsFalse()
    {
        Assert.False(JunctionManager.IsJunction(@"C:\NonExistent\Path\12345"));
    }

    [Fact]
    public void IsJunction_RegularDirectory_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            Assert.False(JunctionManager.IsJunction(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void GetJunctionTarget_NonJunction_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            Assert.Null(JunctionManager.GetJunctionTarget(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }

    [Fact]
    public void GetJunctionTarget_NonExistentPath_ReturnsNull()
    {
        Assert.Null(JunctionManager.GetJunctionTarget(@"C:\NonExistent\12345"));
    }

    [Fact]
    public void CreateJunction_ExistingNonJunctionDirectory_ThrowsInvalidOperation()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var targetDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(targetDir);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(
                () => JunctionManager.CreateJunction(tempDir, targetDir));
            Assert.Contains("目录已存在且不是联接点", ex.Message);
        }
        finally
        {
            Directory.Delete(tempDir);
            Directory.Delete(targetDir);
        }
    }

    [Fact]
    public void RemoveJunction_NonJunctionPath_DoesNothing()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            // Should not throw - just silently does nothing
            JunctionManager.RemoveJunction(tempDir);
            Assert.True(Directory.Exists(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir);
        }
    }
}
