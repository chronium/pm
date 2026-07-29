using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace PM.AgentRuns;

public interface IAgentRunnerCommandPrompts
{
    string ReadPairingCode();
    bool Confirm(string prompt);
}

public sealed class AgentRunnerCommandPrompts : IAgentRunnerCommandPrompts
{
    public string ReadPairingCode()
    {
        if (Console.IsInputRedirected) return Console.In.ReadLine()?.Trim() ?? string.Empty;
        return AnsiConsole.Prompt(new TextPrompt<string>("Pairing code:").Secret());
    }

    public bool Confirm(string prompt) => AnsiConsole.Confirm(prompt, false);
}

public sealed class AgentRunnerPairCommand(
    IAgentRunnerClient runners,
    IAgentRunnerCommandPrompts prompts) : AsyncCommand<AgentRunnerPairCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        if (settings.DryRun) return AgentRunnerCommandOutput.Fail("Runner pairing does not support --dry-run.");
        if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint))
            return AgentRunnerCommandOutput.Fail("Runner endpoint must be an absolute HTTPS URL.");
        if (settings.Replace && !settings.Yes &&
            !prompts.Confirm($"Replace the existing registration for {settings.RunnerId}?")) return 1;

        var code = prompts.ReadPairingCode();
        var result = await runners.Pair(new AgentRunnerPairingRequest(
            endpoint,
            settings.RunnerId.Trim(),
            settings.Fingerprint.Trim().ToLowerInvariant(),
            code,
            settings.Replace), cancellationToken);
        if (!result.Success) return AgentRunnerCommandOutput.Fail(result.Message);
        AgentRunnerCommandOutput.Registration(result.Payload!, "Paired");
        return 0;
    }

    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<https-url>")]
        [Description("Runner HTTPS origin")]
        public string Endpoint { get; init; } = string.Empty;

        [CommandOption("--runner-id <ID>")]
        [Description("Stable runner ID shown by the host pairing command")]
        public string RunnerId { get; init; } = string.Empty;

        [CommandOption("--fingerprint <SHA256>")]
        [Description("TLS certificate fingerprint shown by the host pairing command")]
        public string Fingerprint { get; init; } = string.Empty;

        [CommandOption("--replace")]
        [Description("Explicitly replace an existing registration after successful pairing")]
        public bool Replace { get; init; }

        [CommandOption("--yes")]
        [Description("Confirm replacement without prompting")]
        public bool Yes { get; init; }
    }
}

public sealed class AgentRunnerListCommand(IAgentRunnerClient runners) : Command<CommonSettings>
{
    public override int Execute(CommandContext context, CommonSettings settings,
        CancellationToken cancellationToken)
    {
        var result = runners.Registrations();
        if (!result.Success) return AgentRunnerCommandOutput.Fail(result.Message);
        if (result.Payload!.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No agent runners are registered.[/]");
            return 0;
        }

        var table = new Table()
            .AddColumn("Runner")
            .AddColumn("Name")
            .AddColumn("Endpoint")
            .AddColumn("Protocol")
            .AddColumn("Paired");
        foreach (var runner in result.Payload)
            table.AddRow(
                runner.RunnerId.EscapeMarkup(),
                runner.DisplayName.EscapeMarkup(),
                runner.Endpoint.AbsoluteUri.EscapeMarkup(),
                runner.ProtocolVersion.ToString().EscapeMarkup(),
                runner.PairedAt.ToString("u").EscapeMarkup());
        AnsiConsole.Write(table);
        return 0;
    }
}

public sealed class AgentRunnerStatusCommand(IAgentRunnerClient runners)
    : AsyncCommand<AgentRunnerStatusCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        var health = await runners.Health(settings.RunnerId, cancellationToken);
        if (!health.Success) return AgentRunnerCommandOutput.Fail(health.Message);
        var capabilities = await runners.Capabilities(settings.RunnerId, cancellationToken);
        if (!capabilities.Success) return AgentRunnerCommandOutput.Fail(capabilities.Message);
        var runner = capabilities.Payload!;

        AnsiConsole.MarkupLineInterpolated($"Runner: [green]{runner.RunnerId.EscapeMarkup()}[/]");
        AnsiConsole.MarkupLineInterpolated($"Name: {runner.DisplayName.EscapeMarkup()}");
        AnsiConsole.MarkupLineInterpolated($"Status: [green]{health.Payload!.Status.EscapeMarkup()}[/]");
        if (health.Payload.Build is { } build)
            AnsiConsole.MarkupLineInterpolated(
                $"Build: {build.Version.EscapeMarkup()} ({build.SourceRevision[..Math.Min(12, build.SourceRevision.Length)].EscapeMarkup()})");
        AnsiConsole.MarkupLineInterpolated(
            $"Capacity: {runner.Capacity.ActiveRuns}/{runner.Capacity.MaximumRuns} active");
        AnsiConsole.MarkupLineInterpolated(
            $"Runtime: {runner.ContainerRuntime.EngineId.EscapeMarkup()} {runner.ContainerRuntime.Version.EscapeMarkup()} ({runner.OperatingSystem.EscapeMarkup()} {runner.Architecture.EscapeMarkup()})");
        AnsiConsole.MarkupLineInterpolated(
            $"Providers: {string.Join(", ", runner.AgentProviders.Select(item => item.ProviderId)).EscapeMarkup()}");
        AnsiConsole.MarkupLineInterpolated(
            $"Profiles: {string.Join(", ", runner.RuntimeProfiles.Select(item => item.ProfileId)).EscapeMarkup()}");
        return 0;
    }

    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<runner-id>")]
        public string RunnerId { get; init; } = string.Empty;
    }
}

public sealed class AgentRunnerRotateCommand(
    IAgentRunnerClient runners,
    IAgentRunnerCommandPrompts prompts) : AsyncCommand<AgentRunnerRotateCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        if (settings.DryRun) return AgentRunnerCommandOutput.Fail("Runner credential rotation does not support --dry-run.");
        if (!settings.Yes && !prompts.Confirm($"Rotate the credential for {settings.RunnerId}?")) return 1;
        var result = await runners.Rotate(settings.RunnerId, cancellationToken);
        if (!result.Success) return AgentRunnerCommandOutput.Fail(result.Message);
        AgentRunnerCommandOutput.Registration(result.Payload!, "Rotated");
        return 0;
    }

    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<runner-id>")]
        public string RunnerId { get; init; } = string.Empty;

        [CommandOption("--yes")]
        public bool Yes { get; init; }
    }
}

public sealed class AgentRunnerRevokeCommand(
    IAgentRunnerClient runners,
    IAgentRunnerCommandPrompts prompts) : AsyncCommand<AgentRunnerRevokeCommand.Settings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings,
        CancellationToken cancellationToken)
    {
        if (settings.DryRun) return AgentRunnerCommandOutput.Fail("Runner revocation does not support --dry-run.");
        if (!settings.Yes && !prompts.Confirm(
                $"Revoke {settings.RunnerId} and remove its local registration?")) return 1;
        var result = await runners.Revoke(settings.RunnerId, cancellationToken);
        if (!result.Success) return AgentRunnerCommandOutput.Fail(result.Message);
        AnsiConsole.MarkupLineInterpolated($"Revoked runner [green]{settings.RunnerId.EscapeMarkup()}[/].");
        return 0;
    }

    public sealed class Settings : CommonSettings
    {
        [CommandArgument(0, "<runner-id>")]
        public string RunnerId { get; init; } = string.Empty;

        [CommandOption("--yes")]
        public bool Yes { get; init; }
    }
}

internal static class AgentRunnerCommandOutput
{
    public static int Fail(string? message)
    {
        AnsiConsole.MarkupLineInterpolated(
            $"[red]{(message ?? "Agent runner request failed.").EscapeMarkup()}[/]");
        return 1;
    }

    public static void Registration(AgentRunnerRegistration runner, string action)
    {
        AnsiConsole.MarkupLineInterpolated($"{action} runner [green]{runner.RunnerId.EscapeMarkup()}[/].");
        AnsiConsole.MarkupLineInterpolated($"Name: {runner.DisplayName.EscapeMarkup()}");
        AnsiConsole.MarkupLineInterpolated($"Endpoint: {runner.Endpoint.AbsoluteUri.EscapeMarkup()}");
        AnsiConsole.MarkupLineInterpolated($"TLS fingerprint: {runner.TlsFingerprint.EscapeMarkup()}");
        AnsiConsole.MarkupLineInterpolated($"Client fingerprint: {runner.ClientFingerprint.EscapeMarkup()}");
    }
}
