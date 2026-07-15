using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;

namespace PM.Web;

public static class AngularWebEndpoints
{
    private const string ImmutableCache = "public, max-age=31536000, immutable";
    private const string RevalidateCache = "public, max-age=0, must-revalidate";
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private static readonly Regex FingerprintedFile = new(
        @"(?:^|/)[^/]*[.-][A-Za-z0-9_-]{8,}\.[^/]+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void MapAngularWeb(this IEndpointRouteBuilder endpoints, IAngularAssetStore assets)
    {
        endpoints.MapMethods("/{**path}", [HttpMethods.Get, HttpMethods.Head],
            (HttpContext context, string? path) => Serve(context, assets, path));
    }

    private static async Task Serve(HttpContext context, IAngularAssetStore assets, string? path)
    {
        var requestedPath = (path ?? string.Empty).TrimStart('/');
        if (!IsSafePath(requestedPath) || IsApiPath(requestedPath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (TryGetLegacyRedirect(requestedPath, out var redirectPath))
        {
            context.Response.Redirect(redirectPath + context.Request.QueryString);
            return;
        }

        var assetPath = string.IsNullOrEmpty(requestedPath) ? "index.html" : requestedPath;
        if (!assets.TryGet(assetPath, out var asset))
        {
            if (LooksLikeStaticFile(requestedPath) || !assets.TryGet("index.html", out asset))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            assetPath = "index.html";
        }

        context.Response.ContentType = ContentTypes.TryGetContentType(assetPath, out var contentType)
            ? contentType
            : "application/octet-stream";
        context.Response.ContentLength = asset.Content.Length;
        context.Response.Headers.CacheControl = IsFingerprinted(assetPath) ? ImmutableCache : RevalidateCache;

        if (!HttpMethods.IsHead(context.Request.Method))
            await context.Response.Body.WriteAsync(asset.Content, context.RequestAborted);
    }

    private static bool IsSafePath(string path)
    {
        if (path.Contains('\\', StringComparison.Ordinal)) return false;

        return path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment is not "." and not "..");
    }

    private static bool IsApiPath(string path)
    {
        var firstSegment = path.Split('/', 2)[0];
        return string.Equals(firstSegment, "api", StringComparison.OrdinalIgnoreCase)
               || string.Equals(firstSegment, "openapi", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeStaticFile(string path)
    {
        var lastSegment = path.Split('/').LastOrDefault() ?? string.Empty;
        return path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
               || lastSegment.Contains('.', StringComparison.Ordinal);
    }

    private static bool IsFingerprinted(string path) =>
        !string.Equals(path, "index.html", StringComparison.Ordinal)
        && FingerprintedFile.IsMatch(path);

    private static bool TryGetLegacyRedirect(string path, out string redirectPath)
    {
        redirectPath = string.Empty;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 2 && segments[0] == "task" && segments[1] == "new")
        {
            redirectPath = "/tasks/new";
            return true;
        }

        if (segments.Length is 2 or 3 && segments[0] == "task"
            && (segments.Length == 2 || segments[2] == "edit"))
        {
            redirectPath = $"/tasks/{Uri.EscapeDataString(segments[1])}";
            return true;
        }

        return false;
    }
}
