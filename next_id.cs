#!dotnet run
#:sdk Microsoft.NET.Sdk.Web
#:package Microsoft.Data.Sqlite@10.0.1

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});
// Config (override with env vars if you want)
var dbPath = Environment.GetEnvironmentVariable("DB_PATH") ?? "projects.db";
var listenUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://0.0.0.0:8080";
builder.WebHost.UseUrls(listenUrls);

var app = builder.Build();

// Ensure DB + schema
var csb = new SqliteConnectionStringBuilder
{
    DataSource = dbPath,
    Mode = SqliteOpenMode.ReadWriteCreate,
    Cache = SqliteCacheMode.Shared
};
var connString = csb.ToString();

using (var conn = new SqliteConnection(connString))
{
    conn.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
    PRAGMA journal_mode = WAL;
    PRAGMA synchronous = NORMAL;

    CREATE TABLE IF NOT EXISTS projects (
        key_hash BLOB PRIMARY KEY,   -- SHA-512 = 64 bytes
        next_id  INTEGER NOT NULL
    );
    """;
    cmd.ExecuteNonQuery();
}

// Helpers: URL-safe Base64 (so the key can safely sit in a route segment)
static string ToBase64Url(ReadOnlySpan<byte> bytes)
{
    var s = Convert.ToBase64String(bytes);
    s = s.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    return s;
}
static byte[] FromBase64Url(string s)
{
    s = s.Replace('-', '+').Replace('_', '/');
    switch (s.Length % 4)
    {
        case 2: s += "=="; break;
        case 3: s += "="; break;
        case 0: break;
        default: throw new FormatException("Invalid base64url length.");
    }
    return Convert.FromBase64String(s);
}

static byte[] Sha512(byte[] bytes) => SHA512.HashData(bytes);

app.MapPost("/projects", () =>
{
    // Generate 64 random bytes, return as base64url, store SHA-512(key) + next_id
    Span<byte> keyBytes = stackalloc byte[64];
    RandomNumberGenerator.Fill(keyBytes);

    var key = ToBase64Url(keyBytes);
    var hash = Sha512(keyBytes.ToArray());

    using var conn = new SqliteConnection(connString);
    conn.Open();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "INSERT INTO projects(key_hash, next_id) VALUES($h, 1);";
    cmd.Parameters.AddWithValue("$h", hash);

    try
    {
        cmd.ExecuteNonQuery();
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
    {
        // Extremely unlikely (hash collision / duplicate). Retry once.
        RandomNumberGenerator.Fill(keyBytes);
        key = ToBase64Url(keyBytes);
        hash = Sha512(keyBytes.ToArray());

        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.ExecuteNonQuery();
    }

    // returns 200 + key string body; change to Results.Created if you prefer
    return Results.Ok(new ProjectKeyResponse(key));
});

app.MapGet("/projects/{key}/nextid", (string key) =>
{
    byte[] keyBytes;
    try
    {
        keyBytes = FromBase64Url(key);
    }
    catch
    {
        return Results.Unauthorized();
    }

    var hash = Sha512(keyBytes);

    using var conn = new SqliteConnection(connString);
    conn.Open();

    // Atomic fetch+increment
    using var tx = conn.BeginTransaction(System.Data.IsolationLevel.Serializable);

    long current;
    using (var sel = conn.CreateCommand())
    {
        sel.Transaction = tx;
        sel.CommandText = "SELECT next_id FROM projects WHERE key_hash = $h;";
        sel.Parameters.AddWithValue("$h", hash);
        var obj = sel.ExecuteScalar();
        if (obj is null || obj is DBNull)
        {
            tx.Rollback();
            return Results.Unauthorized();
        }
        current = Convert.ToInt64(obj);
    }

    using (var upd = conn.CreateCommand())
    {
        upd.Transaction = tx;
        upd.CommandText = "UPDATE projects SET next_id = next_id + 1 WHERE key_hash = $h;";
        upd.Parameters.AddWithValue("$h", hash);
        upd.ExecuteNonQuery();
    }

    tx.Commit();
    return Results.Ok(new NextIdResponse(current));
});

app.MapGet("/health", () => "ok");

app.Run();

public record ProjectKeyResponse(string key);
public record NextIdResponse(long id);

[JsonSerializable(typeof(ProjectKeyResponse))]
[JsonSerializable(typeof(NextIdResponse))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}