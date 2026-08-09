using YamlDotNet.Serialization;

namespace PM.Project;

public static class OverviewLayouts
{
    public const string Single = "single";
    public const string Split = "split";
}

public static class OverviewSectionKinds
{
    public const string Hero = "hero";
    public const string Milestone = "milestone";
    public const string Tasks = "tasks";
    public const string Wiki = "wiki";
    public const string Markdown = "markdown";
    public const string Copyright = "copyright";

    public static bool IsSupported(string? value) =>
        value is Hero or Milestone or Tasks or Wiki or Markdown or Copyright;
}

public sealed record OverviewSiteDefinition
{
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public bool? Enabled { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Title { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Description { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public OverviewHomeDefinition? Home { get; set; }
}

public sealed record OverviewHomeDefinition
{
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Layout { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public List<OverviewSectionDefinition>? Sections { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public List<OverviewSectionDefinition>? Primary { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public List<OverviewSectionDefinition>? Secondary { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public List<OverviewSectionDefinition>? After { get; set; }
}

public sealed record OverviewSectionDefinition
{
    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Type { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Title { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Milestone { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Filter { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public int? Limit { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public List<string>? Pages { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Source { get; set; }

    [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitNull)]
    public string? Notice { get; set; }
}
