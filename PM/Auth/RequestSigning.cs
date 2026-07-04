using System.Security.Cryptography;
using System.Text;

namespace PM.Auth;

public sealed record SignedRequestHeaders(
    string UserId,
    string Timestamp,
    string Nonce,
    string Signature,
    string PublicKey);

public static class RequestSigning
{
    private const string AuthVersion = "pm-auth-v1";

    public static SignedRequestHeaders Sign(PmIdentity identity, HttpMethod method, Uri uri, string body)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = $"nonce_{Base64Url(RandomNumberGenerator.GetBytes(18))}";
        var canonical = Canonical(method.Method, uri.AbsolutePath, timestamp, nonce, identity.UserId, body);

        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Base64UrlDecode(identity.PrivateKey), out _);
        var signature = key.SignData(Encoding.UTF8.GetBytes(canonical), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return new SignedRequestHeaders(identity.UserId, timestamp, nonce, Base64Url(signature), identity.PublicKey);
    }

    private static string Canonical(string method, string path, string timestamp, string nonce, string userId, string body)
    {
        var bodyHash = Sha256Hex(Encoding.UTF8.GetBytes(body));
        return string.Join('\n', AuthVersion, method.ToUpperInvariant(), path, timestamp, nonce, userId, bodyHash);
    }

    public static string GeneratePublicId(string prefix)
    {
        return $"{prefix}_{Base64Url(RandomNumberGenerator.GetBytes(18))}";
    }

    public static string GenerateRecoveryKey()
    {
        return $"pmrec_{Base64Url(RandomNumberGenerator.GetBytes(32))}";
    }

    public static string Sha256Hex(string value)
    {
        return Sha256Hex(Encoding.UTF8.GetBytes(value));
    }

    private static string Sha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        var padded = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}
