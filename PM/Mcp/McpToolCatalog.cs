using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace PM.Mcp;

public static class McpToolCatalog
{
    public static IReadOnlySet<string> RunWorkerToolNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "get_project",
        "list_tracks",
        "list_milestones",
        "list_states",
        "validate_project",
        "list_tasks",
        "search_tasks",
        "get_next_task",
        "get_task",
        "list_wiki_pages",
        "search_wiki_pages",
        "get_wiki_page",
        "outline_wiki_page",
        "append_task_note",
    };

    public static IReadOnlyList<McpServerTool> CreateRunWorkerTools()
    {
        var methods = typeof(PmMcpTools)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Select(method => (Method: method, Attribute: method.GetCustomAttribute<McpServerToolAttribute>()))
            .Where(item => item.Attribute?.Name != null && RunWorkerToolNames.Contains(item.Attribute.Name))
            .OrderBy(item => item.Attribute!.Name, StringComparer.Ordinal)
            .ToList();

        var discoveredNames = methods.Select(item => item.Attribute!.Name!).ToHashSet(StringComparer.Ordinal);
        if (!discoveredNames.SetEquals(RunWorkerToolNames))
        {
            var missing = RunWorkerToolNames.Except(discoveredNames, StringComparer.Ordinal);
            throw new InvalidOperationException(
                $"The run-worker MCP tool allowlist contains unknown tools: {string.Join(", ", missing)}.");
        }

        return methods
            .Select(item => McpServerTool.Create(item.Method,
                request => request.Services!.GetRequiredService<PmMcpTools>()))
            .ToList();
    }
}
