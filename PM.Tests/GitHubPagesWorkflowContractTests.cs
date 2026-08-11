namespace PM.Tests;

public sealed class GitHubPagesWorkflowContractTests
{
    [Fact]
    public void PagesWorkflowConsumesThePublishedActionAfterReleasePromotion()
    {
        var workflow = ReadWorkflow();

        Assert.Contains("workflow_run:", workflow);
        Assert.Contains("workflows: [\"Publish PM Action release\"]", workflow);
        Assert.Contains("types: [completed]", workflow);
        Assert.Contains("workflow_dispatch:", workflow);
        Assert.Contains("action-ref:", workflow);
        Assert.Contains("default: latest", workflow);
        Assert.Contains("UPSTREAM_CONCLUSION", workflow);
        Assert.Contains("UPSTREAM_EVENT", workflow);
        Assert.Contains("UPSTREAM_HEAD_SHA", workflow);
        Assert.Contains("action_sha\" != \"$source_revision", workflow);
    }

    [Fact]
    public void PagesWorkflowValidatesBeforeExportingWithLatest()
    {
        var workflow = ReadWorkflow();
        var doctor = workflow.IndexOf("id: doctor_latest", StringComparison.Ordinal);
        var export = workflow.IndexOf("id: site_latest", StringComparison.Ordinal);
        var upload = workflow.IndexOf("actions/upload-pages-artifact@", StringComparison.Ordinal);

        Assert.Equal(2, Count(workflow, "uses: chronium/pm@latest"));
        Assert.True(doctor >= 0 && export > doctor && upload > export);
        Assert.Contains("command: doctor", workflow);
        Assert.Contains("command: site-build", workflow);
        Assert.DoesNotContain("actions/setup-dotnet@", workflow);
        Assert.DoesNotContain("actions/setup-node@", workflow);
        Assert.DoesNotContain("npm run release", workflow);
        Assert.DoesNotContain("dotnet artifacts/release/PM.dll", workflow);
        Assert.DoesNotContain("docker pull", workflow);
    }

    [Fact]
    public void PagesWorkflowSupportsOnlyVerifiedPublishedRollbackRefs()
    {
        var workflow = ReadWorkflow();

        Assert.Contains("latest, vMAJOR.MINOR.PATCH, or full commit SHA", workflow);
        Assert.Contains("grep -Eq '^v[0-9]+\\.[0-9]+\\.[0-9]+$'", workflow);
        Assert.Contains("grep -Eq '^[0-9a-f]{40}$'", workflow);
        Assert.Contains(".commit.verification.verified", workflow);
        Assert.Contains("github-action/release/current.json?ref=$action_sha", workflow);
        Assert.Contains("repository: ${{ github.repository }}", workflow);
        Assert.Contains("ref: ${{ needs.inspect.outputs.action_sha }}", workflow);
        Assert.Contains("uses: ./.pm-published-action", workflow);
        Assert.Contains("test \"$current_sha\" = \"$ACTION_SHA\"", workflow);
    }

    [Fact]
    public void PagesWorkflowPreservesDeploymentAndRecordsReleaseIdentity()
    {
        var workflow = ReadWorkflow();

        Assert.Contains("actions/configure-pages@", workflow);
        Assert.Contains("refs/heads/gh-pages", workflow);
        Assert.Contains("actions/upload-pages-artifact@", workflow);
        Assert.Contains("actions/deploy-pages@", workflow);
        Assert.Contains("Resolved Action revision", workflow);
        Assert.Contains("PM version", workflow);
        Assert.Contains("OCI digest", workflow);
        Assert.Contains("Pages and gh-pages tree", workflow);
    }

    private static string ReadWorkflow()
    {
        var repositoryRoot = FindRepositoryRoot();
        return File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "pages.yml"));
    }

    private static int Count(string value, string fragment)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(fragment, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }

        return count;
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
}
