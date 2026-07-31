using Microsoft.Extensions.DependencyInjection;
using PM;
using PM.Application;
using PM.Mcp;
using PM.Project;

namespace PM.Tests;

public sealed class LinkedProjectRegistryTests
{
    [Fact]
    public async Task ExactOpenDoesNotDiscoverAncestorProject()
    {
        using var workspace = new TempWorkingDirectory();
        var root = await CreateProject(workspace.Path, "prj_parent");
        var childPath = Path.Combine(root.RepositoryPath, "child");
        Directory.CreateDirectory(childPath);

        var opened = ProjectRoot.TryOpenExact(childPath, out _);

        Assert.False(opened);
    }

    [Fact]
    public async Task RegistryBindsVerifiedExactProjectAndPersistsPrivateJson()
    {
        using var workspace = new TempWorkingDirectory();
        var repository = await CreateProject(Path.Combine(workspace.Path, "repository"), "prj_one");
        var registryPath = Path.Combine(workspace.Path, "registry");
        var registry = Registry(registryPath);

        var bound = registry.Bind("prj_one", repository.RepositoryPath);
        var read = registry.Get("prj_one");

        Assert.True(bound.Success);
        Assert.True(read.Success);
        Assert.Equal(repository.RepositoryPath, read.Payload!.RepositoryPath);
        Assert.False(read.Payload.WriteTrusted);
        Assert.Single(registry.List().Payload!);
        Assert.True(File.Exists(Path.Combine(registryPath, "prj_one.json")));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(registryPath, "prj_one.json")));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(registryPath));
        }
    }

    [Fact]
    public async Task RegistryRejectsIdentityMismatchAndRequiresReplaceForChangedBinding()
    {
        using var workspace = new TempWorkingDirectory();
        var first = await CreateProject(Path.Combine(workspace.Path, "first"), "prj_one");
        var second = await CreateProject(Path.Combine(workspace.Path, "second"), "prj_one");
        var other = await CreateProject(Path.Combine(workspace.Path, "other"), "prj_other");
        var registry = Registry(Path.Combine(workspace.Path, "registry"));

        Assert.True(registry.Bind("prj_one", first.RepositoryPath).Success);
        Assert.Equal("project_identity_mismatch", registry.Bind("prj_one", other.RepositoryPath).ErrorCode);
        Assert.Equal("project_binding_exists", registry.Bind("prj_one", second.RepositoryPath).ErrorCode);

        var replaced = registry.Bind("prj_one", second.RepositoryPath, replace: true);

        Assert.True(replaced.Success);
        Assert.Equal(second.RepositoryPath, registry.Get("prj_one").Payload!.RepositoryPath);
        Assert.False(replaced.Payload!.WriteTrusted);
    }

    [Fact]
    public async Task RegistryRejectsSymlinkedStorage()
    {
        if (OperatingSystem.IsWindows()) return;
        using var workspace = new TempWorkingDirectory();
        var repository = await CreateProject(Path.Combine(workspace.Path, "repository"), "prj_one");
        var target = Path.Combine(workspace.Path, "registry-target");
        var link = Path.Combine(workspace.Path, "registry-link");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(link, target);

        var result = Registry(link).Bind("prj_one", repository.RepositoryPath);

        Assert.Equal("insecure_project_registry", result.ErrorCode);
    }

    [Fact]
    public async Task ResolverPrefersRegistryOverPathHint()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await CreateProject(Path.Combine(workspace.Path, "active"), "prj_active");
        var registered = await CreateProject(Path.Combine(workspace.Path, "registered"), "prj_linked");
        var hinted = await CreateProject(Path.Combine(workspace.Path, "hinted"), "prj_linked");
        var registry = Registry(Path.Combine(workspace.Path, "registry"));
        Assert.True(registry.Bind("prj_linked", registered.RepositoryPath).Success);
        var resolver = Resolver(registry);

        var result = await resolver.ResolveAsync(active, Declaration(
            "prj_linked", Path.GetRelativePath(active.RepositoryPath, hinted.RepositoryPath)));

        Assert.Equal(LinkedProjectResolutionStatus.Available, result.Status);
        Assert.Equal(LinkedProjectResolutionSource.Registry, result.Source);
        Assert.Equal(registered.RepositoryPath, result.RepositoryPath);
    }

    [Fact]
    public async Task ResolverFallsBackFromStaleRegistryAndRefreshesFromPathHint()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await CreateProject(Path.Combine(workspace.Path, "active"), "prj_active");
        var stale = await CreateProject(Path.Combine(workspace.Path, "stale"), "prj_linked");
        var hinted = await CreateProject(Path.Combine(workspace.Path, "hinted"), "prj_linked");
        var registry = Registry(Path.Combine(workspace.Path, "registry"));
        Assert.True(registry.Bind("prj_linked", stale.RepositoryPath).Success);
        Directory.Delete(stale.RepositoryPath, true);
        var resolver = Resolver(registry);

        var result = await resolver.ResolveAsync(active, Declaration(
            "prj_linked", Path.GetRelativePath(active.RepositoryPath, hinted.RepositoryPath)));

        Assert.Equal(LinkedProjectResolutionStatus.Available, result.Status);
        Assert.Equal(LinkedProjectResolutionSource.PathHint, result.Source);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "stale_project_binding");
        Assert.Equal(hinted.RepositoryPath, registry.Get("prj_linked").Payload!.RepositoryPath);
        Assert.False(registry.Get("prj_linked").Payload!.WriteTrusted);
    }

    [Fact]
    public async Task ResolverNeverExposesMismatchedProject()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await CreateProject(Path.Combine(workspace.Path, "active"), "prj_active");
        var wrong = await CreateProject(Path.Combine(workspace.Path, "wrong"), "prj_wrong");
        var resolver = Resolver(Registry(Path.Combine(workspace.Path, "registry")));

        var result = await resolver.ResolveAsync(active, Declaration(
            "prj_expected", Path.GetRelativePath(active.RepositoryPath, wrong.RepositoryPath)));

        Assert.Equal(LinkedProjectResolutionStatus.IdentityMismatch, result.Status);
        Assert.Null(result.Project);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "project_identity_mismatch");
    }

    [Fact]
    public async Task ResolverReturnsMissingForAbsentPathHintAndStaleForLoneBrokenBinding()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await CreateProject(Path.Combine(workspace.Path, "active"), "prj_active");
        var stale = await CreateProject(Path.Combine(workspace.Path, "stale"), "prj_stale");
        var registry = Registry(Path.Combine(workspace.Path, "registry"));
        Assert.True(registry.Bind("prj_stale", stale.RepositoryPath).Success);
        Directory.Delete(stale.RepositoryPath, true);
        var resolver = Resolver(registry);

        var missing = await resolver.ResolveAsync(active, Declaration("prj_missing", "../missing"));
        var staleResult = await resolver.ResolveAsync(active, Declaration("prj_stale", null));

        Assert.Equal(LinkedProjectResolutionStatus.Missing, missing.Status);
        Assert.Equal(LinkedProjectResolutionStatus.Missing, staleResult.Status);
        Assert.Null(missing.Project);
        Assert.Null(staleResult.Project);
    }

    [Fact]
    public async Task ResolverReportsIdentityMismatchWhenRegisteredRepositoryChangesIdentity()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await CreateProject(Path.Combine(workspace.Path, "active"), "prj_active");
        var linked = await CreateProject(Path.Combine(workspace.Path, "linked"), "prj_linked");
        var registry = Registry(Path.Combine(workspace.Path, "registry"));
        Assert.True(registry.Bind("prj_linked", linked.RepositoryPath).Success);
        await File.WriteAllTextAsync(Path.Combine(linked.RootPath, GlobalConfig.ProjectIdFile), "prj_other\n");

        var result = await Resolver(registry).ResolveAsync(active, Declaration("prj_linked", null));

        Assert.Equal(LinkedProjectResolutionStatus.IdentityMismatch, result.Status);
        Assert.Null(result.Project);
    }

    [Fact]
    public async Task ResolverTreatsMalformedExactProjectAsInvalid()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await CreateProject(Path.Combine(workspace.Path, "active"), "prj_active");
        var broken = await CreateProject(Path.Combine(workspace.Path, "broken"), "prj_broken");
        await File.WriteAllTextAsync(broken.ConfigPath, "name: [unterminated");
        var resolver = Resolver(Registry(Path.Combine(workspace.Path, "registry")));

        var result = await resolver.ResolveAsync(active, Declaration(
            "prj_broken", Path.GetRelativePath(active.RepositoryPath, broken.RepositoryPath)));

        Assert.Equal(LinkedProjectResolutionStatus.Invalid, result.Status);
        Assert.Null(result.Project);
    }

    [Fact]
    public async Task ResolverOffersArgumentVectorForUninitializedSubmodule()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await CreateProject(Path.Combine(workspace.Path, "active"), "prj_active");
        var pathHint = "linked game";
        var repair = new LinkedProjectRepairAction(
            "git", ["submodule", "update", "--init", "--", pathHint], "git submodule update --init -- 'linked game'");
        var resolver = new LinkedProjectResolver(
            Registry(Path.Combine(workspace.Path, "registry")),
            new StubSubmoduleInspector(repair));

        var result = await resolver.ResolveAsync(active, Declaration("prj_linked", pathHint));

        Assert.Equal(LinkedProjectResolutionStatus.UninitializedSubmodule, result.Status);
        Assert.Equal(["submodule", "update", "--init", "--", pathHint], result.RepairAction!.Arguments);
        Assert.Contains("'linked game'", result.RepairAction.DisplayCommand);
    }

    [Fact]
    public async Task BindAndUnbindCommandsResolveAliasesAndStableIds()
    {
        using var workspace = new TempWorkingDirectory();
        var active = await CreateProject(Path.Combine(workspace.Path, "active"), "prj_active");
        var linked = await CreateProject(Path.Combine(workspace.Path, "linked"), "prj_linked");
        active.WriteLinkedProjectsManifest(new LinkedProjectManifest
        {
            Children = [Declaration("prj_linked", "../linked")],
        });
        var service = new LinkedProjectService(active);
        var registry = Registry(Path.Combine(workspace.Path, "registry"));

        var bindExit = new ProjectBindCommand(active, service, registry).Execute(null!, new ProjectBindCommand.Settings
        {
            Selector = "linked",
            RepositoryPath = linked.RepositoryPath,
        }, CancellationToken.None);
        active.DeleteLinkedProjectsManifest();
        var unbindExit = new ProjectUnbindCommand(active, service, registry).Execute(null!,
            new ProjectUnbindCommand.Settings { Selector = "prj_linked" }, CancellationToken.None);

        Assert.Equal(0, bindExit);
        Assert.Equal(0, unbindExit);
        Assert.Equal("project_not_registered", registry.Get("prj_linked").ErrorCode);
    }

    [Fact]
    public async Task McpProjectOpenAutomaticallyRefreshesRegistry()
    {
        using var workspace = new TempWorkingDirectory();
        var project = await CreateProject(workspace.Path, "prj_active");
        var registryPath = Path.Combine(workspace.Path, "registry");
        var previousRegistryPath = Environment.GetEnvironmentVariable("PM_PROJECT_REGISTRY_PATH");
        var previousDirectory = Environment.CurrentDirectory;
        Environment.SetEnvironmentVariable("PM_PROJECT_REGISTRY_PATH", registryPath);
        Environment.CurrentDirectory = project.RepositoryPath;

        try
        {
            using var host = McpServerHost.CreateBuilder([]).Build();
            _ = host.Services.GetRequiredService<ProjectRoot>();

            var registered = new LinkedProjectRegistryStore(
                new LinkedProjectRegistryStoreOptions { RootPath = registryPath }).Get("prj_active");
            Assert.True(registered.Success);
            Assert.Equal(project.RepositoryPath, registered.Payload!.RepositoryPath);
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Environment.SetEnvironmentVariable("PM_PROJECT_REGISTRY_PATH", previousRegistryPath);
        }
    }

    [Theory]
    [InlineData("simple", "git submodule update --init -- simple")]
    [InlineData("two words", "git submodule update --init -- 'two words'")]
    public void RepairDisplayCommandQuotesUnsafeArguments(string path, string expected)
    {
        var actual = GitLinkedProjectSubmoduleInspector.FormatDisplayCommand(
            "git", ["submodule", "update", "--init", "--", path]);

        if (OperatingSystem.IsWindows())
            Assert.Contains(path.Contains(' ') ? "\"two words\"" : path, actual);
        else
            Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task GitInspectorDetectsDeclaredSubmoduleAndReturnsSafeRepairAction()
    {
        using var workspace = new TempWorkingDirectory();
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, ".gitmodules"), """
                                                                                     [submodule "linked game"]
                                                                                       path = linked game
                                                                                       url = https://example.test/linked.git
                                                                                     """);

        var result = await new GitLinkedProjectSubmoduleInspector().InspectAsync(
            workspace.Path, "linked game", CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotNull(result.Payload);
        Assert.Equal(["submodule", "update", "--init", "--", "linked game"], result.Payload!.Arguments);
    }

    private static LinkedProjectRegistryStore Registry(string path) =>
        new(new LinkedProjectRegistryStoreOptions { RootPath = path });

    private static LinkedProjectResolver Resolver(LinkedProjectRegistryStore registry) =>
        new(registry, new StubSubmoduleInspector(null));

    private static LinkedProjectDeclaration Declaration(string projectId, string? pathHint) => new()
    {
        ProjectId = projectId,
        Alias = "linked",
        RepositoryUrl = $"https://example.test/{projectId}.git",
        PathHint = pathHint,
    };

    private static async Task<ProjectRoot> CreateProject(string repositoryPath, string projectId)
    {
        Directory.CreateDirectory(repositoryPath);
        var previous = Environment.CurrentDirectory;
        Environment.CurrentDirectory = repositoryPath;
        try
        {
            var root = new ProjectRoot();
            await root.CreateProject(TestData.Config());
            await File.WriteAllTextAsync(Path.Combine(root.RootPath, GlobalConfig.ProjectIdFile), $"{projectId}\n");
            return root;
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    private sealed class StubSubmoduleInspector(LinkedProjectRepairAction? repairAction)
        : ILinkedProjectSubmoduleInspector
    {
        public Task<AppResult<LinkedProjectRepairAction?>> InspectAsync(
            string repositoryPath,
            string pathHint,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AppResult<LinkedProjectRepairAction?>.Ok(repairAction));
    }
}
