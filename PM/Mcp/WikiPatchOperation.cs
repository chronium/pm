using System.Text.Json.Serialization;

namespace PM.Mcp;

[JsonConverter(typeof(JsonStringEnumConverter<WikiPatchOperation>))]
public enum WikiPatchOperation
{
    [JsonStringEnumMemberName("append_to_section")]
    AppendToSection,

    [JsonStringEnumMemberName("prepend_to_section")]
    PrependToSection,

    [JsonStringEnumMemberName("replace_section_body")]
    ReplaceSectionBody,

    [JsonStringEnumMemberName("insert_before_heading")]
    InsertBeforeHeading,

    [JsonStringEnumMemberName("insert_after_section")]
    InsertAfterSection,
}
