#!dotnet run
#:sdk Microsoft.NET.Sdk.Web
#:package Microsoft.Data.Sqlite@10.0.1

using System.Buffers.Text;
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
        next_id  INTEGER
    );

    CREATE TABLE IF NOT EXISTS project_counters (
        key_hash BLOB NOT NULL,
        track    TEXT NOT NULL,
        next_id  INTEGER NOT NULL,
        PRIMARY KEY (key_hash, track)
    );
    """;
    cmd.ExecuteNonQuery();
}

static byte[] Sha512(byte[] bytes) => SHA512.HashData(bytes);

app.MapPost("/projects", () =>
{
    // Generate 64 random bytes, return as base64url, store SHA-512(key) + next_id
    Span<byte> keyBytes = stackalloc byte[64];
    RandomNumberGenerator.Fill(keyBytes);

    var key = Base64Url.EncodeToString(keyBytes);
    var hash = Sha512(keyBytes.ToArray());

    using var conn = new SqliteConnection(connString);
    conn.Open();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = "INSERT INTO projects(key_hash, next_id) VALUES($h, NULL);";
    cmd.Parameters.AddWithValue("$h", hash);

    try
    {
        cmd.ExecuteNonQuery();
    }
    catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
    {
        // Extremely unlikely (hash collision / duplicate). Retry once.
        RandomNumberGenerator.Fill(keyBytes);
        key = Base64Url.EncodeToString(keyBytes);
        hash = Sha512(keyBytes.ToArray());

        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("$h", hash);
        cmd.ExecuteNonQuery();
    }

    // returns 200 + key string body; change to Results.Created if you prefer
    return Results.Ok(new ProjectKeyResponse(key));
});

app.MapGet("/projects/{key}/tracks/{track}/nextid", (string key, string track) =>
{
    if (!TryGetProjectHash(key, out var hash)) return Results.Unauthorized();

    using var conn = new SqliteConnection(connString);
    conn.Open();

    // Atomic fetch+increment
    using var tx = conn.BeginTransaction(System.Data.IsolationLevel.Serializable);

    long current;
    using (var sel = conn.CreateCommand())
    {
        sel.Transaction = tx;
        sel.CommandText = """
                          INSERT INTO project_counters(key_hash, track, next_id)
                          SELECT key_hash, $track, COALESCE(next_id, 1)
                          FROM projects
                          WHERE key_hash = $h
                          ON CONFLICT(key_hash, track) DO NOTHING;

                          SELECT next_id FROM project_counters WHERE key_hash = $h AND track = $track;
                          """;
        sel.Parameters.AddWithValue("$h", hash);
        sel.Parameters.AddWithValue("$track", track);
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
        upd.CommandText = "UPDATE project_counters SET next_id = next_id + 1 WHERE key_hash = $h AND track = $track;";
        upd.Parameters.AddWithValue("$h", hash);
        upd.Parameters.AddWithValue("$track", track);
        upd.ExecuteNonQuery();
    }

    tx.Commit();
    return Results.Ok(new NextIdResponse(current));
});

app.MapGet("/projects/{key}/tracks/{track}/peekid", (string key, string track) =>
{
    if (!TryGetProjectHash(key, out var hash)) return Results.Unauthorized();

    using var conn = new SqliteConnection(connString);
    conn.Open();

    using var cmd = conn.CreateCommand();
    cmd.CommandText = """
                      INSERT INTO project_counters(key_hash, track, next_id)
                      SELECT key_hash, $track, COALESCE(next_id, 1)
                      FROM projects
                      WHERE key_hash = $h
                      ON CONFLICT(key_hash, track) DO NOTHING;

                      SELECT next_id FROM project_counters WHERE key_hash = $h AND track = $track;
                      """;
    cmd.Parameters.AddWithValue("$h", hash);
    cmd.Parameters.AddWithValue("$track", track);
    var obj = cmd.ExecuteScalar();

    if (obj is null || obj is DBNull)
    {
        return Results.Unauthorized();
    }

    var current = Convert.ToInt64(obj);
    return Results.Ok(new NextIdResponse(current));
});

app.MapGet("/health", () => "ok");

app.Run();

static bool TryGetProjectHash(string key, out byte[] hash)
{
    hash = [];
    try
    {
        var keyBytes = Base64Url.DecodeFromChars(key);
        hash = Sha512(keyBytes);
        return true;
    }
    catch
    {
        return false;
    }
}

public record ProjectKeyResponse(string key);
public record NextIdResponse(long id);

[JsonSerializable(typeof(ProjectKeyResponse))]
[JsonSerializable(typeof(NextIdResponse))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
