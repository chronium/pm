using System.Text.Json.Serialization;

namespace PM.Mcp;

[JsonConverter(typeof(JsonStringEnumConverter<ActivationRequirementInputKind>))]
public enum ActivationRequirementInputKind
{
    [JsonStringEnumMemberName("task")]
    Task,

    [JsonStringEnumMemberName("milestone")]
    Milestone,
}
