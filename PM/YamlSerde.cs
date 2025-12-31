using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PM;

public static class YamlSerde
{
    public static ISerializer Serializer { get; } = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static IDeserializer Deserializer { get; } = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static string Serialize(object obj)
    {
        return Serializer.Serialize(obj);
    }

    public static T Deserialize<T>(string yaml)
    {
        return Deserializer.Deserialize<T>(yaml);
    }
}