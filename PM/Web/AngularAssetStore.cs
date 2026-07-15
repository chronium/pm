using System.Reflection;

namespace PM.Web;

public sealed record AngularAsset(byte[] Content);

public interface IAngularAssetStore
{
    bool HasAssets { get; }
    bool TryGet(string path, out AngularAsset asset);
}

public sealed class EmbeddedAngularAssetStore : IAngularAssetStore
{
    private const string ResourcePrefix = "PM.AngularAssets/";
    private readonly IReadOnlyDictionary<string, AngularAsset> assets;

    public EmbeddedAngularAssetStore(Assembly? assembly = null)
    {
        assembly ??= typeof(EmbeddedAngularAssetStore).Assembly;
        assets = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .ToDictionary(
                name => name[ResourcePrefix.Length..],
                name => ReadAsset(assembly, name),
                StringComparer.Ordinal);
    }

    public bool HasAssets => assets.ContainsKey("index.html");

    public bool TryGet(string path, out AngularAsset asset) => assets.TryGetValue(path, out asset!);

    private static AngularAsset ReadAsset(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded Angular resource '{resourceName}' could not be read.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new AngularAsset(buffer.ToArray());
    }
}
