using YamlDotNet.Serialization;

namespace PM.Project;

public sealed record LinkedProjectDeclaration
{
    public string ProjectId { get; init; } = string.Empty;
    public string Alias { get; init; } = string.Empty;
    public string RepositoryUrl { get; init; } = string.Empty;

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? PathHint { get; init; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? PublicSiteUrl { get; init; }
}

public sealed record LinkedProjectManifest
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public LinkedProjectDeclaration? Parent { get; init; }

    public List<LinkedProjectDeclaration> Children { get; init; } = [];
}
