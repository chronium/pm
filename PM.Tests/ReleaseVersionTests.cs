using System.Reflection;
using PM.Application;
using PM.Project;

namespace PM.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.0.0", 0, 0, 0)]
    [InlineData("1.0.1\n", 1, 0, 1)]
    [InlineData("12.34.56\r\n", 12, 34, 56)]
    [InlineData("65534.65534.65534", 65534, 65534, 65534)]
    public void ParserAcceptsCanonicalReleaseVersions(
        string content,
        int expectedMajor,
        int expectedMinor,
        int expectedPatch)
    {
        var parsed = ReleaseVersion.TryParse(content, out var version, out var error);

        Assert.True(parsed, error);
        Assert.Equal(new ReleaseVersion(expectedMajor, expectedMinor, expectedPatch), version);
        Assert.Equal($"{expectedMajor}.{expectedMinor}.{expectedPatch}", version!.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("01.0.0")]
    [InlineData("1.00.0")]
    [InlineData("1.0.01")]
    [InlineData("+1.0.0")]
    [InlineData("1.-1.0")]
    [InlineData(" 1.0.0")]
    [InlineData("1.0.0 ")]
    [InlineData("1.0.0\n\n")]
    [InlineData("1.0.0-beta")]
    [InlineData("65535.0.0")]
    [InlineData("999999999999999999999999.0.0")]
    public void ParserRejectsNonCanonicalReleaseVersions(string content)
    {
        var parsed = ReleaseVersion.TryParse(content, out var version, out var error);

        Assert.False(parsed);
        Assert.Null(version);
        Assert.NotEmpty(error!);
    }

    [Fact]
    public async Task MissingReleaseVersionOptsProjectOut()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();

        var result = new ReleaseVersionService(projectRoot).Read();

        Assert.True(result.Success);
        Assert.False(result.Payload!.Enabled);
        Assert.Null(result.Payload.Version);
        Assert.True(new ProjectValidationService(projectRoot).ValidateProject().Payload!.Valid);
    }

    [Fact]
    public async Task ReleaseVersionServiceReadsCanonicalVersion()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        await File.WriteAllTextAsync(projectRoot.ReleaseVersionPath, "2.3.4\n");

        var result = new ReleaseVersionService(projectRoot).Read();

        Assert.True(result.Success);
        Assert.True(result.Payload!.Enabled);
        Assert.Equal(new ReleaseVersion(2, 3, 4), result.Payload.Version);
    }

    [Fact]
    public async Task DoctorReportsMalformedReleaseVersion()
    {
        using var workspace = new TempWorkingDirectory();
        var projectRoot = await workspace.CreateProject();
        await File.WriteAllTextAsync(projectRoot.ReleaseVersionPath, "v2.3.4\n");

        var result = new ProjectValidationService(projectRoot).ValidateProject();

        Assert.True(result.Success);
        Assert.False(result.Payload!.Valid);
        var issue = Assert.Single(result.Payload.Issues, issue => issue.Code == "invalid_release_version");
        Assert.Equal(projectRoot.ReleaseVersionPath, issue.Path);
    }

    [Fact]
    public void TrackedVersionMatchesRuntimeAndAssemblyIdentity()
    {
        var repositoryRoot = Path.GetFullPath("../../../..", AppContext.BaseDirectory);
        var content = File.ReadAllText(Path.Combine(repositoryRoot, ".pm", GlobalConfig.ReleaseVersionFile));
        Assert.True(ReleaseVersion.TryParse(content, out var version, out var error), error);

        var expected = version!.ToString();
        var assembly = typeof(GlobalConfig).Assembly;
        Assert.Equal(expected, GlobalConfig.ApplicationVersion);
        Assert.Equal(expected,
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
        Assert.Equal($"{expected}.0",
            assembly.GetCustomAttribute<AssemblyFileVersionAttribute>()!.Version);
        Assert.Equal(new Version(version.Major, version.Minor, version.Patch, 0), assembly.GetName().Version);
    }
}
