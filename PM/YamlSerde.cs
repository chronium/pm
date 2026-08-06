using YamlDotNet.Serialization;
using YamlDotNet.Serialization.Converters;
using YamlDotNet.Serialization.NamingConventions;
using PM.Project;

namespace PM;

public static class YamlSerde
{
    public static ISerializer Serializer { get; } = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithEnumNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new DateTimeOffsetConverter())
        .Build();

    public static IDeserializer Deserializer { get; } = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithEnumNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new DateTimeOffsetConverter())
        .Build();

    public static string Serialize(object obj)
    {
        var yaml = Serializer.Serialize(obj);
        return obj is ProjectConfig
            ? yaml
                .Replace("    delivery: \n", "    delivery: null\n", StringComparison.Ordinal)
                .Replace("    activation: \n", "    activation: null\n", StringComparison.Ordinal)
            : yaml;
    }

    public static T Deserialize<T>(string yaml)
    {
        if (typeof(T) == typeof(ProjectConfig))
            return (T)(object)ProjectConfig.Deserialize(yaml);

        return Deserializer.Deserialize<T>(yaml);
    }
}
