using System.Reflection;

namespace PM.Web;

internal static class TemplateStore
{
    private static readonly Assembly Assembly = typeof(TemplateStore).Assembly;

    public static string Read(string fileName)
    {
        var resourceName = Assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith($".Templates.{fileName}", StringComparison.Ordinal));

        if (resourceName == null)
            throw new InvalidOperationException($"Template resource {fileName} was not found.");

        using var stream = Assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Template resource {fileName} could not be read.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

