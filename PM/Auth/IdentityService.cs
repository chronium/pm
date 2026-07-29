using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PM.Files;

namespace PM.Auth;

public sealed record PmIdentity(
    string UserId,
    string DisplayName,
    string PrivateKey,
    string PublicKey,
    DateTime CreatedAt);

public interface IIdentityService
{
    PmIdentity GetOrCreateIdentity();
}

public sealed class IdentityService(IdentityServiceOptions? options = null) : IIdentityService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IdentityServiceOptions _options = options ?? new IdentityServiceOptions();

    public PmIdentity GetOrCreateIdentity()
    {
        var identityPath = _options.IdentityPath ?? GetDefaultIdentityPath();
        if (FileSystem.FileExists(identityPath))
        {
            var json = FileSystem.ReadAllText(identityPath);
            return JsonSerializer.Deserialize<PmIdentity>(json, JsonOptions)
                   ?? throw new InvalidOperationException("PM identity file is invalid.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(identityPath)!);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var identity = new PmIdentity(
            $"usr_{Base64Url(RandomNumberGenerator.GetBytes(18))}",
            Environment.UserName,
            Base64Url(key.ExportPkcs8PrivateKey()),
            Base64Url(key.ExportSubjectPublicKeyInfo()),
            DateTime.UtcNow);

        FileSystem.WriteAllText(identityPath, JsonSerializer.Serialize(identity, JsonOptions));
        return identity;
    }

    private static string GetDefaultIdentityPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("PM_IDENTITY_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) return Path.GetFullPath(overridePath);

        return Path.Combine(UserConfigurationPaths.GetPmDirectory(), "identity.json");
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

public sealed class IdentityServiceOptions
{
    public string? IdentityPath { get; init; }
}
