using System.Security.Cryptography;
using System.Text;
using PM.Auth;

namespace PM.AgentRuns;

public sealed record AgentRunnerCredential(
    string ClientId,
    string DisplayName,
    string PrivateKey,
    string PublicKey,
    DateTimeOffset CreatedAt)
{
    public static AgentRunnerCredential FromPmIdentity(PmIdentity identity) =>
        new(identity.UserId, identity.DisplayName, identity.PrivateKey, identity.PublicKey,
            new DateTimeOffset(identity.CreatedAt.ToUniversalTime()));

    public static AgentRunnerCredential Generate(string displayName)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new AgentRunnerCredential(
            $"usr_{AgentRunnerEncoding.Base64Url(RandomNumberGenerator.GetBytes(18))}",
            displayName,
            AgentRunnerEncoding.Base64Url(key.ExportPkcs8PrivateKey()),
            AgentRunnerEncoding.Base64Url(key.ExportSubjectPublicKeyInfo()),
            DateTimeOffset.UtcNow);
    }

    public string Fingerprint => AgentRunnerRequestSigning.PublicKeyFingerprint(PublicKey);
}

public sealed record AgentRunnerSignedHeaders(
    string ClientId,
    string Timestamp,
    string Nonce,
    string Signature,
    string ProtocolVersion);

public static class AgentRunnerRequestSigning
{
    private const string AuthVersion = "pm-runner-auth-v1";
    private const string RotationVersion = "pm-runner-rotation-v1";

    public static AgentRunnerSignedHeaders Sign(
        AgentRunnerCredential credential,
        HttpMethod method,
        string pathAndQuery,
        ReadOnlySpan<byte> body,
        AgentRunProtocolVersion protocolVersion,
        DateTimeOffset? timestamp = null,
        string? nonce = null)
    {
        var timestampValue = (timestamp ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds().ToString();
        var nonceValue = nonce ?? $"nonce_{AgentRunnerEncoding.Base64Url(RandomNumberGenerator.GetBytes(18))}";
        var canonical = string.Join('\n',
            AuthVersion,
            method.Method.ToUpperInvariant(),
            pathAndQuery,
            protocolVersion.ToString(),
            timestampValue,
            nonceValue,
            credential.ClientId,
            Sha256Hex(body));

        return new AgentRunnerSignedHeaders(
            credential.ClientId,
            timestampValue,
            nonceValue,
            AgentRunnerEncoding.Sign(credential.PrivateKey, Encoding.UTF8.GetBytes(canonical)),
            protocolVersion.ToString());
    }

    public static string SignRotationProof(
        AgentRunnerCredential successor,
        string runnerId,
        string oldClientId,
        string requestNonce)
    {
        var canonical = string.Join('\n', RotationVersion, runnerId, oldClientId,
            successor.ClientId, successor.PublicKey, requestNonce);
        return AgentRunnerEncoding.Sign(successor.PrivateKey, Encoding.UTF8.GetBytes(canonical));
    }

    public static string PublicKeyFingerprint(string publicKey)
    {
        var bytes = AgentRunnerEncoding.Base64UrlDecode(publicKey);
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(bytes, out var read);
        if (read != bytes.Length) throw new CryptographicException("Runner public key is invalid.");
        return $"sha256:{Sha256Hex(key.ExportSubjectPublicKeyInfo())}";
    }

    public static bool CredentialMatches(AgentRunnerCredential credential)
    {
        try
        {
            var challenge = RandomNumberGenerator.GetBytes(32);
            var signature = AgentRunnerEncoding.Sign(credential.PrivateKey, challenge);
            using var publicKey = ECDsa.Create();
            publicKey.ImportSubjectPublicKeyInfo(AgentRunnerEncoding.Base64UrlDecode(credential.PublicKey), out _);
            return publicKey.VerifyData(challenge, AgentRunnerEncoding.Base64UrlDecode(signature),
                HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return false;
        }
    }

    public static string Sha256Hex(ReadOnlySpan<byte> value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

internal static class AgentRunnerEncoding
{
    public static string Sign(string privateKey, ReadOnlySpan<byte> value)
    {
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(Base64UrlDecode(privateKey), out _);
        return Base64Url(key.SignData(value, HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    public static string Base64Url(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        var padded = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}
