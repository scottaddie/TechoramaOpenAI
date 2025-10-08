namespace TechoramaOpenAI.Models;

public class AzureOpenAISettings
{
    public required string DeploymentName { get; set; } = string.Empty;
    public required string Endpoint { get; set; } = string.Empty;
    public required string Scope { get; set; } = string.Empty;
}