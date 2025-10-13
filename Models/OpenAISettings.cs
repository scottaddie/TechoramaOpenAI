namespace TechoramaOpenAI.Models;

public class OpenAISettings
{
    public required string ModelName { get; set; } = string.Empty;

    public Dictionary<string, List<McpToolInfo>> McpToolsListed { get; set; } = new();

    public List<string> McpToolsUsed { get; set; } = new();
}
