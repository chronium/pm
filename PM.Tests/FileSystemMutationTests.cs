using PM.Files;

namespace PM.Tests;

public sealed class FileSystemMutationTests
{
    [Fact]
    public void TrackedMutationsRejectOutsidePathsBeforeChangingFiles()
    {
        using var workspace = new TempWorkingDirectory();
        var trackedRoot = Path.Combine(workspace.Path, "tracked");
        var outsideRoot = Path.Combine(workspace.Path, "outside");
        Directory.CreateDirectory(trackedRoot);
        Directory.CreateDirectory(outsideRoot);
        var path = Path.Combine(outsideRoot, "task.ref");
        File.WriteAllText(path, "original");

        using var mutations = FileSystem.TrackMutations(trackedRoot);

        Assert.Throws<InvalidOperationException>(() => FileSystem.WriteAllText(path, "changed"));
        Assert.Equal("original", File.ReadAllText(path));
        Assert.Throws<InvalidOperationException>(() => FileSystem.DeleteFile(path));
        Assert.True(File.Exists(path));
        Assert.Empty(mutations.ChangedPaths);
    }
}
