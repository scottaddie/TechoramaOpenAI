namespace TechoramaOpenAI.Models;

public class McpToolInfo : IEquatable<McpToolInfo>
{
    public required string Name { get; set; }

    public required string Annotations { get; set; }

    public bool Equals(McpToolInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Name == other.Name;
    }

    public override bool Equals(object? obj) => Equals(obj as McpToolInfo);

    public override int GetHashCode() => Name.GetHashCode();
}
