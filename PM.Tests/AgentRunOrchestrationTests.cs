using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using PM.AgentRuns;
using PM.Application;
using PM.Project;

namespace PM.Tests;

public class AgentRunOrchestrationTests
{
    [Fact]
    public async Task PrivateCachePersistsDraftRemoteStateAndReplayCursor()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await Project(workspace);
        var cacheRoot = Path.Combine(workspace.Path, "run-cache");
        var cache = new AgentRunCache(root, TimeProvider.System,
            new AgentRunCacheOptions { RootPath = cacheRoot });
        var request = Request("run-cache-test", "runner-test", "project-test");
        var selection = Selection("runner-test");

        Assert.True((await cache.SaveDraft(selection, request)).Success);
        var loaded = await cache.Get(request.Specification.RunId);
        Assert.True(loaded.Success, loaded.Message);
        Assert.Equal(request.SpecificationHash, loaded.Payload!.Request.SpecificationHash);
        Assert.Null(loaded.Payload.RemoteRun);

        var remote = RemoteRun(request, AgentRunState.Running, 3);
        Assert.True((await cache.UpdateRemote(request.Specification.RunId, remote)).Success);
        Assert.True((await cache.AdvanceSequence(request.Specification.RunId, 4)).Success);

        var restarted = new AgentRunCache(root, TimeProvider.System,
            new AgentRunCacheOptions { RootPath = cacheRoot });
        var afterRestart = await restarted.Get(request.Specification.RunId);
        Assert.Equal(AgentRunState.Running, afterRestart.Payload!.RemoteRun!.State);
        Assert.Equal(4, afterRestart.Payload.LastObservedSequence);

        if (!OperatingSystem.IsWindows())
        {
            var projectDirectory = Assert.Single(Directory.GetDirectories(cacheRoot));
            var file = Assert.Single(Directory.GetFiles(projectDirectory, "*.json"));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(projectDirectory));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
    }

    [Fact]
    public async Task CacheSerializesConcurrentCursorAndRemoteUpdatesAndRejectsMalformedFiles()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await Project(workspace);
        var cacheRoot = Path.Combine(workspace.Path, "run-cache");
        var cache = new AgentRunCache(root, TimeProvider.System,
            new AgentRunCacheOptions { RootPath = cacheRoot });
        var request = Request("run-concurrent-test", "runner-test", "project-test");
        Assert.True((await cache.SaveDraft(Selection("runner-test"), request)).Success);

        var mutations = Enumerable.Range(1, 30).Select(async sequence =>
        {
            if (sequence % 2 == 0)
                await cache.UpdateRemote(request.Specification.RunId,
                    RemoteRun(request, AgentRunState.Running, sequence));
            else
                await cache.AdvanceSequence(request.Specification.RunId, sequence);
        });
        await Task.WhenAll(mutations);

        var current = await cache.Get(request.Specification.RunId);
        Assert.True(current.Success, current.Message);
        Assert.Equal(30, current.Payload!.LastObservedSequence);

        var file = Directory.GetFiles(Assert.Single(Directory.GetDirectories(cacheRoot)), "*.json").Single();
        await File.WriteAllTextAsync(file, "{}");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var malformed = await cache.Get(request.Specification.RunId);
        Assert.False(malformed.Success);
        Assert.Equal("invalid_run_cache", malformed.ErrorCode);
    }

    [Fact]
    public async Task PreflightPersistsExactRequestAndStartRejectsTaskDriftBeforeRunnerContact()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await Project(workspace);
        var git = new FakeGitInspector();
        var runner = new FakeRunnerClient("runner-test");
        var cache = new AgentRunCache(root, TimeProvider.System,
            new AgentRunCacheOptions { RootPath = Path.Combine(workspace.Path, "cache") });
        var service = new AgentRunService(root, new BoardService(root), git, cache, runner, TimeProvider.System);

        var preflight = await service.Preflight(Selection("runner-test"));

        Assert.True(preflight.Success, preflight.Message);
        Assert.True(preflight.Payload!.Ready);
        Assert.NotNull(preflight.Payload.RunId);
        Assert.Equal(git.Snapshot.TaskRevision, preflight.Payload.Request!.Specification.Task.Revision);
        Assert.Equal("task-execution", preflight.Payload.Request.Specification.Agent.PromptProfileId);
        Assert.All(preflight.Payload.Checks, check => Assert.NotEqual(AgentRunPreflightCheckStatus.Failed, check.Status));

        git.Snapshot = git.Snapshot with { TaskRevision = new string('9', 64) };
        var stale = await service.Start(preflight.Payload.RunId!, preflight.Payload.Revision!);

        Assert.False(stale.Success);
        Assert.Equal("stale_run_preflight", stale.ErrorCode);
        Assert.Equal(0, runner.StartCalls);
        Assert.True(root.TryGetById("PM-0001", out _));
        Assert.True(root.TryGetState(root.GetAllTasks().Single(), out var state));
        Assert.Equal("todo", state);
    }

    [Fact]
    public async Task LinkedWikiSelectionIsImmutableAndRunnerVerifiedBeforePreflightAndStart()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await Project(workspace);
        var git = new FakeGitInspector();
        var runner = new FakeRunnerClient("runner-test");
        var linked = new AgentRunLinkedContext(
            "project-engine",
            "Shared engine",
            "engine",
            new AgentRunRepository("git@github.com:chronium/engine.git", new string('c', 40)),
            AgentRunLinkedContextRequirement.Required,
            [AgentRunLinkedContextScope.Wiki]);
        var resolver = new FakeLinkedContextResolver(linked);
        var service = new AgentRunService(root, new BoardService(root), git,
            new AgentRunCache(root, TimeProvider.System,
                new AgentRunCacheOptions { RootPath = Path.Combine(workspace.Path, "cache") }),
            runner, TimeProvider.System, resolver);
        var selection = Selection("runner-test") with
        {
            LinkedContexts =
            [
                new AgentRunLinkedContextSelection(
                    "project-engine", AgentRunLinkedContextRequirement.Required),
            ],
        };

        var preflight = await service.Preflight(selection);

        Assert.True(preflight.Success, preflight.Message);
        Assert.True(preflight.Payload!.Ready);
        Assert.Equal(linked, Assert.Single(preflight.Payload.Request!.Specification.LinkedContexts!));
        Assert.Equal(1, runner.PreflightCalls);
        Assert.Equal(preflight.Payload.Request.SpecificationHash,
            runner.LastPreflightRequest!.SpecificationHash);

        var started = await service.Start(preflight.Payload.RunId!, preflight.Payload.Revision!);
        Assert.True(started.Success, started.Message);
        Assert.Equal(2, runner.PreflightCalls);
        Assert.Equal(1, runner.StartCalls);
    }

    [Fact]
    public async Task RepeatedStartUsesRunnerIdempotencyForThePersistedRequest()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await Project(workspace);
        var git = new FakeGitInspector();
        var runner = new FakeRunnerClient("runner-test");
        var service = new AgentRunService(root, new BoardService(root), git,
            new AgentRunCache(root, TimeProvider.System,
                new AgentRunCacheOptions { RootPath = Path.Combine(workspace.Path, "cache") }),
            runner, TimeProvider.System);
        var preflight = (await service.Preflight(Selection("runner-test"))).Payload!;

        var first = await service.Start(preflight.RunId!, preflight.Revision!);
        var second = await service.Start(preflight.RunId!, preflight.Revision!);

        Assert.True(first.Success, first.Message);
        Assert.Equal(AgentRunRemoteStartDisposition.New, first.Payload!.Disposition);
        Assert.True(second.Success, second.Message);
        Assert.Equal(AgentRunRemoteStartDisposition.Existing, second.Payload!.Disposition);
        Assert.Equal(2, runner.StartCalls);
        Assert.Equal(runner.FirstRequest!.SpecificationHash, runner.LastRequest!.SpecificationHash);
        Assert.Equal(
            AgentRunCanonicalJson.WriteSpecification(runner.FirstRequest.Specification),
            AgentRunCanonicalJson.WriteSpecification(runner.LastRequest.Specification));
    }

    [Fact]
    public async Task ExpectedReadinessFailuresReturnNamedChecksWithoutCreatingDrafts()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await Project(workspace);
        var git = new FakeGitInspector { Ready = false };
        var runner = new FakeRunnerClient("runner-test");
        var cacheRoot = Path.Combine(workspace.Path, "cache");
        var service = new AgentRunService(root, new BoardService(root), git,
            new AgentRunCache(root, TimeProvider.System, new AgentRunCacheOptions { RootPath = cacheRoot }),
            runner, TimeProvider.System);

        var result = await service.Preflight(Selection("runner-test"));

        Assert.True(result.Success);
        Assert.False(result.Payload!.Ready);
        Assert.Null(result.Payload.RunId);
        Assert.Contains(result.Payload.Checks,
            check => check.Id == "worktree" && check.Status == AgentRunPreflightCheckStatus.Failed);
        Assert.False(Directory.Exists(cacheRoot));
        Assert.Equal(0, runner.HealthCalls);
    }

    [Fact]
    public async Task GitInspectorRejectsDirtyWorktreesAndLocalUpstreamsAsReadinessChecks()
    {
        using var workspace = new TempWorkingDirectory();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".pm", "tasks"));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, ".pm", GlobalConfig.PmConfigFile), "name: Test\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, ".pm", "project_id.txt"), "project-test\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, ".pm", "tasks", "PM-0001.md"), "# Test\n");
        Git(workspace.Path, "init", "-b", "main");
        Git(workspace.Path, "config", "user.name", "PM tests");
        Git(workspace.Path, "config", "user.email", "pm-tests@example.test");
        Git(workspace.Path, "add", ".");
        Git(workspace.Path, "commit", "-m", "fixture");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "dirty.txt"), "dirty");

        var inspector = new AgentRunGitInspector();
        var dirty = await inspector.Inspect(workspace.Path, "PM-0001");
        Assert.True(dirty.Success);
        Assert.False(dirty.Payload!.Ready);
        Assert.Contains(dirty.Payload.Checks,
            check => check.Id == "worktree" && check.Status == AgentRunPreflightCheckStatus.Failed);

        File.Delete(Path.Combine(workspace.Path, "dirty.txt"));
        Git(workspace.Path, "remote", "add", "origin", Path.Combine(workspace.Path, "local.git"));
        Git(workspace.Path, "config", "branch.main.remote", "origin");
        Git(workspace.Path, "config", "branch.main.merge", "refs/heads/main");
        var localRemote = await inspector.Inspect(workspace.Path, "PM-0001");
        Assert.True(localRemote.Success);
        Assert.False(localRemote.Payload!.Ready);
        Assert.Contains(localRemote.Payload.Checks,
            check => check.Id == "upstream" && check.Status == AgentRunPreflightCheckStatus.Failed);
    }

    [Fact]
    public async Task PatchCollectionPreservesNonOverlappingWorkAndRecordsVerifiedApplication()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await Project(workspace);
        const string remoteUrl = "https://github.com/chronium/pm.git";
        var target = Path.Combine(workspace.Path, "sample.txt");
        await File.WriteAllTextAsync(target, "before\n");
        InitializeRepository(workspace.Path, remoteUrl);
        var head = GitOutput(workspace.Path, "rev-parse", "HEAD").Trim();
        await File.WriteAllTextAsync(target, "after\n");
        var patch = GitBytes(workspace.Path, "diff", "--binary", "--full-index");
        await File.WriteAllTextAsync(target, "before\n");
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "local-notes.txt"), "keep me\n");

        var runner = new FakeRunnerClient("runner-test", patch);
        var cache = await CompletedRunCache(workspace, root, runner, remoteUrl, head);
        var service = new AgentRunService(root, new BoardService(root), new FakeGitInspector(), cache,
            runner, TimeProvider.System);

        var preflight = await service.PreflightPatchCollection("run-patch-test");

        Assert.True(preflight.Success, preflight.Message);
        Assert.True(preflight.Payload!.Ready);
        Assert.Contains(preflight.Payload.Paths, path => path.Path == "sample.txt");
        Assert.Contains(preflight.Payload.Warnings, warning => warning.Contains("non-overlapping"));

        var collected = await service.CollectPatch("run-patch-test", preflight.Payload.Revision,
            preflight.Payload.ArtifactSha256);

        Assert.True(collected.Success, collected.Message);
        Assert.Equal("after\n", await File.ReadAllTextAsync(target));
        Assert.Equal("keep me\n", await File.ReadAllTextAsync(Path.Combine(workspace.Path, "local-notes.txt")));
        Assert.NotNull((await cache.Get("run-patch-test")).Payload!.PatchCollection);
        var repeated = await service.CollectPatch("run-patch-test", preflight.Payload.Revision,
            preflight.Payload.ArtifactSha256);
        Assert.False(repeated.Success);
        Assert.Equal("patch_already_collected", repeated.ErrorCode);
        Assert.True(root.TryGetById("PM-0001", out _));
        Assert.True(root.TryGetState(root.GetAllTasks().Single(), out var state));
        Assert.Equal("todo", state);
    }

    [Fact]
    public async Task PatchCollectionRejectsOverlappingLocalChangesBeforeMutation()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await Project(workspace);
        const string remoteUrl = "https://github.com/chronium/pm.git";
        var target = Path.Combine(workspace.Path, "sample.txt");
        await File.WriteAllTextAsync(target, "before\n");
        InitializeRepository(workspace.Path, remoteUrl);
        var head = GitOutput(workspace.Path, "rev-parse", "HEAD").Trim();
        await File.WriteAllTextAsync(target, "agent change\n");
        var patch = GitBytes(workspace.Path, "diff", "--binary", "--full-index");
        await File.WriteAllTextAsync(target, "local change\n");

        var runner = new FakeRunnerClient("runner-test", patch);
        var cache = await CompletedRunCache(workspace, root, runner, remoteUrl, head);
        var service = new AgentRunService(root, new BoardService(root), new FakeGitInspector(), cache,
            runner, TimeProvider.System);

        var preflight = await service.PreflightPatchCollection("run-patch-test");

        Assert.True(preflight.Success, preflight.Message);
        Assert.False(preflight.Payload!.Ready);
        Assert.Contains(preflight.Payload.Checks,
            check => check.Id == "worktree_overlap" && check.Status == AgentRunPreflightCheckStatus.Failed);
        Assert.Equal("local change\n", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task PatchCollectionRejectsCorruptContentAndPmAuthorityPaths()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await Project(workspace);
        const string remoteUrl = "https://github.com/chronium/pm.git";
        var stateRef = Path.Combine(workspace.Path, ".pm", "states", "todo", "PM-0001.ref");
        var originalStateRef = await File.ReadAllTextAsync(stateRef);
        InitializeRepository(workspace.Path, remoteUrl);
        var head = GitOutput(workspace.Path, "rev-parse", "HEAD").Trim();
        await File.WriteAllTextAsync(stateRef, "PM-0001\nchanged\n");
        var patch = GitBytes(workspace.Path, "diff", "--binary", "--full-index");
        await File.WriteAllTextAsync(stateRef, originalStateRef);

        var authorityRunner = new FakeRunnerClient("runner-test", patch);
        var authorityCache = await CompletedRunCache(workspace, root, authorityRunner, remoteUrl, head);
        var authorityService = new AgentRunService(root, new BoardService(root), new FakeGitInspector(),
            authorityCache, authorityRunner, TimeProvider.System);
        var authority = await authorityService.PreflightPatchCollection("run-patch-test");

        Assert.True(authority.Success, authority.Message);
        Assert.False(authority.Payload!.Ready);
        Assert.Contains(authority.Payload.Checks,
            check => check.Id == "patch_safety" && check.Status == AgentRunPreflightCheckStatus.Failed);

        Directory.Delete(Path.Combine(workspace.Path, ".git", "pm-run-cache"), true);
        var corrupt = patch.ToArray();
        corrupt[^1] ^= 0x01;
        var corruptRunner = new FakeRunnerClient("runner-test", patch, corrupt);
        var corruptCache = await CompletedRunCache(workspace, root, corruptRunner, remoteUrl, head);
        var corruptService = new AgentRunService(root, new BoardService(root), new FakeGitInspector(),
            corruptCache, corruptRunner, TimeProvider.System);
        var corruptResult = await corruptService.PreflightPatchCollection("run-patch-test");

        Assert.False(corruptResult.Success);
        Assert.Equal("artifact_corrupt", corruptResult.ErrorCode);
        Assert.Equal(originalStateRef, await File.ReadAllTextAsync(stateRef));
    }

    private static async Task<AgentRunCache> CompletedRunCache(
        TempWorkingDirectory workspace,
        ProjectRoot root,
        FakeRunnerClient runner,
        string remoteUrl,
        string head)
    {
        var cache = new AgentRunCache(root, TimeProvider.System,
            new AgentRunCacheOptions { RootPath = Path.Combine(workspace.Path, ".git", "pm-run-cache") });
        var request = Request("run-patch-test", "runner-test", "project-test");
        var taskRevision = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(root.GetTaskFilePath("PM-0001")))).ToLowerInvariant();
        var specification = request.Specification with
        {
            Repository = new AgentRunRepository(remoteUrl, head),
            Task = request.Specification.Task with { TaskId = "PM-0001", Revision = taskRevision },
        };
        request = new AgentRunRequest(AgentRunCanonicalJson.ComputeSpecificationHash(specification), specification);
        Assert.True((await cache.SaveDraft(Selection("runner-test"), request)).Success);
        Assert.True((await cache.UpdateRemote(request.Specification.RunId,
            RemoteRun(request, AgentRunState.Completed, 10))).Success);
        runner.SetRun(RemoteRun(request, AgentRunState.Completed, 10));
        return cache;
    }

    private static void InitializeRepository(string path, string remoteUrl)
    {
        Git(path, "init", "-b", "main");
        Git(path, "config", "user.name", "PM tests");
        Git(path, "config", "user.email", "pm-tests@example.test");
        Git(path, "remote", "add", "origin", remoteUrl);
        Git(path, "add", ".");
        Git(path, "commit", "-m", "fixture");
    }

    private static async Task<ProjectRoot> Project(TempWorkingDirectory workspace)
    {
        var root = await workspace.CreateProject();
        await File.WriteAllTextAsync(Path.Combine(root.RootPath, GlobalConfig.ProjectIdFile), "project-test\n");
        var task = TestData.Task("PM-0001", "Run orchestration task", "Do the work.");
        root.WriteTask(task);
        root.UpdateTaskState(task, "todo");
        return root;
    }

    private static AgentRunSelection Selection(string runnerId) =>
        new("PM-0001", runnerId, "dotnet-10", "codex", "gpt-5.6-sol", "medium");

    private static void Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    private static string GitOutput(string directory, params string[] arguments) =>
        System.Text.Encoding.UTF8.GetString(GitBytes(directory, arguments));

    private static byte[] GitBytes(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        using var output = new MemoryStream();
        process.StandardOutput.BaseStream.CopyTo(output);
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output.ToArray();
    }

    private static AgentRunRequest Request(string runId, string runnerId, string projectId)
    {
        var fixture = JsonSerializer.Deserialize<AgentRunRequest>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AgentRunContracts", "v1", "run-request.json")),
            AgentRunJson.Options)!;
        var specification = fixture.Specification with
        {
            RunId = runId,
            Project = fixture.Specification.Project with { ProjectId = projectId },
            Runtime = fixture.Specification.Runtime with { RunnerId = runnerId },
        };
        return new AgentRunRequest(AgentRunCanonicalJson.ComputeSpecificationHash(specification), specification);
    }

    private static AgentRunnerRun RemoteRun(AgentRunRequest request, AgentRunState state, long sequence)
    {
        var now = request.Specification.RequestedAt;
        return new AgentRunnerRun(request.Specification.RunId, request.SpecificationHash, request.Specification,
            state, sequence, now, now, null, null, null);
    }

    private sealed class FakeGitInspector : IAgentRunGitInspector
    {
        public bool Ready { get; init; } = true;
        public AgentRunGitSnapshot Snapshot { get; set; } = new(
            "/repository", "feature", "origin", "git@github.com:chronium/pm.git",
            "refs/heads/feature", new string('a', 40), new string('1', 64));

        public Task<AppResult<AgentRunGitInspection>> Inspect(string projectDirectory, string taskId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            AppResult<AgentRunGitInspection>.Ok(Ready
                ? new AgentRunGitInspection(Snapshot,
                [new AgentRunPreflightCheck("repository", "Git repository",
                    AgentRunPreflightCheckStatus.Passed, "Ready.")])
                : new AgentRunGitInspection(null,
                [new AgentRunPreflightCheck("worktree", "Clean worktree",
                    AgentRunPreflightCheckStatus.Failed, "Commit changes.")])));
    }

    private sealed class FakeLinkedContextResolver(AgentRunLinkedContext context)
        : IAgentRunLinkedContextResolver
    {
        public Task<AppResult<AgentRunLinkedContextResolution>> Resolve(
            IReadOnlyList<AgentRunLinkedContextSelection> selections,
            CancellationToken cancellationToken = default) => Task.FromResult(
            AppResult<AgentRunLinkedContextResolution>.Ok(new AgentRunLinkedContextResolution(
                selections.Count == 0 ? [] : [context],
                selections.Count == 0
                    ? []
                    : [new AgentRunPreflightCheck("linked_context_project-engine", "Linked wiki context",
                        AgentRunPreflightCheckStatus.Passed, "Captured exact revision.")],
                true)));
    }

    private sealed class FakeRunnerClient : IAgentRunnerClient
    {
        private readonly AgentRunnerRegistration _registration;
        private readonly AgentRunnerCapabilities _capabilities;
        private readonly Dictionary<string, AgentRunnerRun> _runs = [];
        private readonly AgentRunArtifact? _artifact;
        private readonly byte[]? _artifactBytes;

        public FakeRunnerClient(string runnerId, byte[]? artifactBytes = null, byte[]? contentBytes = null)
        {
            _registration = new AgentRunnerRegistration(runnerId, "Test runner", new Uri("https://runner.test/"),
                $"sha256:{new string('a', 64)}", AgentRunProtocol.Current, "client-test",
                $"sha256:{new string('b', 64)}", DateTimeOffset.UtcNow);
            _capabilities = JsonSerializer.Deserialize<AgentRunnerCapabilities>(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "AgentRunContracts", "v1", "runner-capabilities.json")),
                AgentRunJson.Options)! with { RunnerId = runnerId };
            _artifactBytes = contentBytes ?? artifactBytes;
            if (artifactBytes != null)
                _artifact = new AgentRunArtifact("changes-patch", "patch", "changes.patch", "text/x-diff",
                    artifactBytes.Length,
                    Convert.ToHexString(SHA256.HashData(artifactBytes)).ToLowerInvariant(),
                    DateTimeOffset.Parse("2026-07-29T08:10:00Z"));
        }

        public void SetRun(AgentRunnerRun run) => _runs[run.RunId] = run;

        public int HealthCalls { get; private set; }
        public int PreflightCalls { get; private set; }
        public int StartCalls { get; private set; }
        public AgentRunRequest? LastPreflightRequest { get; private set; }
        public AgentRunRequest? FirstRequest { get; private set; }
        public AgentRunRequest? LastRequest { get; private set; }
        public AppResult<IReadOnlyList<AgentRunnerRegistration>> Registrations() =>
            AppResult<IReadOnlyList<AgentRunnerRegistration>>.Ok([_registration]);
        public AppResult<AgentRunnerRegistration> Registration(string runnerId) =>
            runnerId == _registration.RunnerId
                ? AppResult<AgentRunnerRegistration>.Ok(_registration)
                : AppResult<AgentRunnerRegistration>.Fail("runner_not_registered", "Runner not registered.");
        public Task<AppResult<AgentRunnerRegistration>> Pair(AgentRunnerPairingRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(AppResult<AgentRunnerRegistration>.Ok(_registration));
        public Task<AppResult<AgentRunnerHealth>> Health(string runnerId,
            CancellationToken cancellationToken = default)
        {
            HealthCalls++;
            return Task.FromResult(AppResult<AgentRunnerHealth>.Ok(new AgentRunnerHealth(runnerId, "online",
                AgentRunProtocol.Current, DateTimeOffset.UtcNow)));
        }
        public Task<AppResult<AgentRunnerCapabilities>> Capabilities(string runnerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<AgentRunnerCapabilities>.Ok(_capabilities));
        public Task<AppResult<AgentRunnerPreflightResult>> Preflight(string runnerId,
            AgentRunRequest request, CancellationToken cancellationToken = default)
        {
            PreflightCalls++;
            LastPreflightRequest = request;
            return Task.FromResult(AppResult<AgentRunnerPreflightResult>.Ok(
                new AgentRunnerPreflightResult(true,
                [new AgentRunPreflightCheck("linked_context_project-engine", "Linked wiki context",
                    AgentRunPreflightCheckStatus.Passed, "Exact commit available.")] )));
        }
        public Task<AppResult<AgentRunRemoteStart>> Start(string runnerId, AgentRunRequest request,
            CancellationToken cancellationToken = default)
        {
            StartCalls++;
            FirstRequest ??= request;
            LastRequest = request;
            var disposition = _runs.TryGetValue(request.Specification.RunId, out var run)
                ? AgentRunRemoteStartDisposition.Existing
                : AgentRunRemoteStartDisposition.New;
            run ??= RemoteRun(request, AgentRunState.Accepted, 1);
            _runs[run.RunId] = run;
            return Task.FromResult(AppResult<AgentRunRemoteStart>.Ok(new AgentRunRemoteStart(disposition, run)));
        }
        public Task<AppResult<AgentRunnerRun>> Inspect(string runnerId, string runId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<AgentRunnerRun>.Ok(_runs[runId]));
        public Task<AppResult<AgentRunnerRunPage>> ActiveRuns(string runnerId, int limit = 100,
            string? cursor = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<AgentRunnerRunPage>.Ok(new AgentRunnerRunPage([], null, false)));
        public Task<AppResult<AgentRunEventPage>> Events(string runnerId, string runId,
            long afterSequence = 0, int limit = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<AgentRunEventPage>.Ok(new AgentRunEventPage([], afterSequence, false, false)));
        public Task<AppResult<IAgentRunnerEventStream>> OpenEventStream(string runnerId, string runId,
            long afterSequence = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<IAgentRunnerEventStream>.Fail("not_implemented", "Not implemented."));
        public Task<AppResult<AgentRunCancellation>> Cancel(string runnerId, string runId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppResult<IReadOnlyList<AgentRunArtifact>>> Artifacts(string runnerId, string runId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            AppResult<IReadOnlyList<AgentRunArtifact>>.Ok(_artifact == null ? [] : [_artifact]));
        public Task<AppResult<AgentRunArtifact>> Artifact(string runnerId, string runId, string artifactId,
            CancellationToken cancellationToken = default) => Task.FromResult(
            _artifact != null && artifactId == _artifact.ArtifactId
                ? AppResult<AgentRunArtifact>.Ok(_artifact)
                : AppResult<AgentRunArtifact>.Fail("artifact_not_found", "Artifact not found."));
        public Task<AppResult<IAgentRunArtifactContent>> ArtifactContent(string runnerId, string runId,
            string artifactId, CancellationToken cancellationToken = default) => Task.FromResult(
            _artifact != null && _artifactBytes != null && artifactId == _artifact.ArtifactId
                ? AppResult<IAgentRunArtifactContent>.Ok(new TestArtifactContent(_artifact, _artifactBytes))
                : AppResult<IAgentRunArtifactContent>.Fail("artifact_not_found", "Artifact not found."));
        public Task<AppResult<AgentRunnerRegistration>> Rotate(string runnerId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AppResult> Revoke(string runnerId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestArtifactContent(AgentRunArtifact artifact, byte[] bytes) : IAgentRunArtifactContent
    {
        public AgentRunArtifact Artifact { get; } = artifact;
        public Stream Content { get; } = new MemoryStream(bytes, writable: false);
        public async ValueTask DisposeAsync() => await Content.DisposeAsync();
    }
}
