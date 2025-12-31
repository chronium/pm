namespace PM.Tasks;

public record TaskItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; init; } = DateTime.UtcNow;
}