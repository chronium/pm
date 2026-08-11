using PM.GitHubAction;
using PM.Project;

namespace PM.Tests;

public sealed class GitHubActionDispatcherTests
{
    [Fact]
    public async Task DoctorDispatchesExactArgumentsAndWritesOutputs()
    {
        using var fixture = new ActionFixture();
        fixture.CreateProject("project");
        var runner = new RecordingProcessRunner();

        var result = await fixture.Dispatch(runner, "doctor", "project", "ignored", "false");

        Assert.Equal(0, result.ExitCode);
        var call = Assert.Single(runner.StreamingCalls);
        Assert.Equal(["doctor"], call.Arguments);
        Assert.Equal(Path.Combine(fixture.CanonicalWorkspace, "project"), call.WorkingDirectory);
        var version = Assert.Single(runner.CapturedCalls);
        Assert.Equal(["--version"], version.Arguments);
        Assert.Equal("pm-version=1.2.3\nsite-path=\n", File.ReadAllText(fixture.OutputFile));
        Assert.Contains("PM `doctor` completed with Project Model 1.2.3.",
            File.ReadAllText(fixture.SummaryFile));
    }

    [Fact]
    public async Task NestedDirectoryUsesUpwardProjectDiscovery()
    {
        using var fixture = new ActionFixture();
        fixture.CreateProject("family/starfall");
        Directory.CreateDirectory(Path.Combine(fixture.Workspace, "family/starfall/src/content"));
        var runner = new RecordingProcessRunner();

        var result = await fixture.Dispatch(
            runner, "doctor", "family/starfall/src/content", "ignored", "false");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(Path.Combine(fixture.CanonicalWorkspace, "family/starfall/src/content"),
            Assert.Single(runner.StreamingCalls).WorkingDirectory);
    }

    [Fact]
    public async Task VersionDoesNotRequireAProjectAndStreamsItsOutput()
    {
        using var fixture = new ActionFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Workspace, "plain"));
        var runner = new RecordingProcessRunner();

        var result = await fixture.Dispatch(runner, "version", "plain", "ignored", "false");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(runner.StreamingCalls);
        Assert.Equal(["--version"], Assert.Single(runner.CapturedCalls).Arguments);
        Assert.Equal("1.2.3\n", result.StandardOutput);
        Assert.Equal("pm-version=1.2.3\nsite-path=\n", File.ReadAllText(fixture.OutputFile));
    }

    [Fact]
    public async Task SiteBuildUsesCanonicalWorkspaceRelativeOutput()
    {
        using var fixture = new ActionFixture();
        fixture.CreateProject("project");
        var runner = new RecordingProcessRunner();

        var result = await fixture.Dispatch(
            runner, "site-build", "project", "dist/project-site", "true");

        Assert.Equal(0, result.ExitCode);
        var call = Assert.Single(runner.StreamingCalls);
        Assert.Equal([
            "site", "build", "--output", Path.Combine(fixture.CanonicalWorkspace, "dist/project-site"), "--force",
        ], call.Arguments);
        Assert.Equal("pm-version=1.2.3\nsite-path=dist/project-site\n",
            File.ReadAllText(fixture.OutputFile));
        Assert.Contains("Site output: `dist/project-site`.", File.ReadAllText(fixture.SummaryFile));
    }

    [Fact]
    public async Task PmFailurePreservesExitCodeAndDoesNotPublishOutputs()
    {
        using var fixture = new ActionFixture();
        fixture.CreateProject("project");
        var runner = new RecordingProcessRunner { StreamingExitCode = 7 };

        var result = await fixture.Dispatch(runner, "doctor", "project", "ignored", "false");

        Assert.Equal(7, result.ExitCode);
        Assert.Empty(runner.CapturedCalls);
        Assert.False(File.Exists(fixture.OutputFile));
        Assert.Contains("failed with exit code 7", File.ReadAllText(fixture.SummaryFile));
    }

    [Theory]
    [InlineData("doctor; touch owned", ".", "ignored", "false", "command must be exactly")]
    [InlineData("doctor", ".", "ignored", "true", "force may be true only")]
    [InlineData("doctor", ".", "ignored", "TRUE", "force must be exactly")]
    [InlineData("doctor", "../outside", "ignored", "false", "parent traversal")]
    [InlineData("doctor", "/tmp", "ignored", "false", "workspace-relative")]
    [InlineData("doctor", "bad\npath", "ignored", "false", "control characters")]
    public async Task InvalidInputsAreRejectedBeforePm(
        string command,
        string workingDirectory,
        string outputDirectory,
        string force,
        string expectedError)
    {
        using var fixture = new ActionFixture();
        fixture.CreateProject(".");
        var runner = new RecordingProcessRunner();

        var result = await fixture.Dispatch(runner, command, workingDirectory, outputDirectory, force);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(runner.StreamingCalls);
        Assert.Empty(runner.CapturedCalls);
        Assert.Contains(expectedError, result.StandardError);
    }

    [Fact]
    public async Task WorkingDirectorySymlinkEscapeIsRejected()
    {
        using var fixture = new ActionFixture();
        var outside = fixture.CreateOutsideDirectory();
        Directory.CreateSymbolicLink(Path.Combine(fixture.Workspace, "escaped"), outside);
        var runner = new RecordingProcessRunner();

        var result = await fixture.Dispatch(runner, "doctor", "escaped", "ignored", "false");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(runner.StreamingCalls);
        Assert.Contains("outside GITHUB_WORKSPACE", result.StandardError);
    }

    [Fact]
    public async Task OutputDirectorySymlinkEscapeIsRejected()
    {
        using var fixture = new ActionFixture();
        fixture.CreateProject("project");
        var outside = fixture.CreateOutsideDirectory();
        Directory.CreateSymbolicLink(Path.Combine(fixture.Workspace, "escaped"), outside);
        var runner = new RecordingProcessRunner();

        var result = await fixture.Dispatch(runner, "site-build", "project", "escaped/site", "false");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(runner.StreamingCalls);
        Assert.Contains("outside GITHUB_WORKSPACE", result.StandardError);
    }

    [Fact]
    public async Task ProjectMetadataSymlinkEscapeIsRejectedBeforeDoctor()
    {
        using var fixture = new ActionFixture();
        var project = Path.Combine(fixture.Workspace, "project");
        Directory.CreateDirectory(project);
        var outside = fixture.CreateOutsideDirectory();
        File.WriteAllText(Path.Combine(outside, GlobalConfig.PmConfigFile),
            YamlSerde.Serialize(TestData.Config()));
        Directory.CreateSymbolicLink(Path.Combine(project, GlobalConfig.PmDirName), outside);
        var runner = new RecordingProcessRunner();

        var result = await fixture.Dispatch(runner, "doctor", "project", "ignored", "false");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(runner.StreamingCalls);
        Assert.Contains(".pm directory must remain within", result.StandardError);
    }

    [Theory]
    [InlineData(".", "workspace root")]
    [InlineData("project", "working-directory")]
    [InlineData("project/.pm/site", ".pm or its descendants")]
    public async Task UnsafeSiteDestinationsAreRejected(string outputDirectory, string expectedError)
    {
        using var fixture = new ActionFixture();
        fixture.CreateProject("project");
        var runner = new RecordingProcessRunner();

        var result = await fixture.Dispatch(runner, "site-build", "project", outputDirectory, "false");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(runner.StreamingCalls);
        Assert.Contains(expectedError, result.StandardError);
    }

    [Fact]
    public async Task MissingGitHubFileCommandsFailBeforePm()
    {
        using var fixture = new ActionFixture();
        fixture.CreateProject(".");
        var runner = new RecordingProcessRunner();
        var dispatcher = new GitHubActionDispatcher(runner, new StringWriter(), new StringWriter());

        var result = await dispatcher.RunAsync(
            ["doctor", ".", "ignored", "false"],
            new GitHubActionEnvironment(fixture.Workspace, null, fixture.SummaryFile));

        Assert.Equal(1, result);
        Assert.Empty(runner.StreamingCalls);
    }

    [Fact]
    public void PromotionTemplateDeclaresTheApprovedFixedInterface()
    {
        var repository = FindRepositoryRoot();
        var metadata = File.ReadAllText(Path.Combine(repository, "action.template.yml"));

        Assert.Contains("image: docker://ghcr.io/chronium/pm@sha256:__PM_ACTION_IMAGE_DIGEST__", metadata);
        Assert.Contains("- ${{ inputs.command }}", metadata);
        Assert.Contains("- ${{ inputs.working-directory }}", metadata);
        Assert.Contains("- ${{ inputs.output-directory }}", metadata);
        Assert.Contains("- ${{ inputs.force }}", metadata);
        Assert.False(File.Exists(Path.Combine(repository, "action.yml")));
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PM.slnx"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate the PM repository root.");
    }

    private sealed class ActionFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"pm-action-tests-{Guid.NewGuid():N}");

        public ActionFixture()
        {
            Workspace = Path.Combine(root, "workspace");
            Directory.CreateDirectory(Workspace);
            CanonicalWorkspace = ActionPathResolver.TryResolveExistingAbsoluteDirectory(Workspace).Payload!;
            OutputFile = Path.Combine(root, "github-output");
            SummaryFile = Path.Combine(root, "github-summary");
        }

        public string Workspace { get; }
        public string CanonicalWorkspace { get; }
        public string OutputFile { get; }
        public string SummaryFile { get; }

        public void CreateProject(string relativePath)
        {
            var project = Path.GetFullPath(Path.Combine(Workspace, relativePath));
            var pmRoot = Path.Combine(project, GlobalConfig.PmDirName);
            Directory.CreateDirectory(pmRoot);
            File.WriteAllText(Path.Combine(pmRoot, GlobalConfig.PmConfigFile),
                YamlSerde.Serialize(TestData.Config()));
        }

        public string CreateOutsideDirectory()
        {
            var path = Path.Combine(root, "outside");
            Directory.CreateDirectory(path);
            return path;
        }

        public async Task<DispatchResult> Dispatch(RecordingProcessRunner runner, params string[] arguments)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var dispatcher = new GitHubActionDispatcher(runner, stdout, stderr);
            var exitCode = await dispatcher.RunAsync(arguments,
                new GitHubActionEnvironment(Workspace, OutputFile, SummaryFile));
            return new DispatchResult(exitCode, stdout.ToString(), stderr.ToString());
        }

        public void Dispose()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed record DispatchResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class RecordingProcessRunner : IPmActionProcessRunner
    {
        public List<ProcessCall> StreamingCalls { get; } = [];
        public List<ProcessCall> CapturedCalls { get; } = [];
        public int StreamingExitCode { get; init; }
        public PmActionProcessResult CapturedResult { get; init; } = new(0, "1.2.3\n", string.Empty);

        public Task<int> RunAsync(
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            StreamingCalls.Add(new ProcessCall([.. arguments], workingDirectory));
            return Task.FromResult(StreamingExitCode);
        }

        public Task<PmActionProcessResult> CaptureAsync(
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            CapturedCalls.Add(new ProcessCall([.. arguments], workingDirectory));
            return Task.FromResult(CapturedResult);
        }
    }

    private sealed record ProcessCall(IReadOnlyList<string> Arguments, string WorkingDirectory);
}
