using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace PM;

public static class YamlSerde
{
    public static ISerializer Serializer { get; } = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static string Serialize(object obj)
    {
        return Serializer.Serialize(obj);
    }
}