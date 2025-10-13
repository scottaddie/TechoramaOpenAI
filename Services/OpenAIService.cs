using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Responses;
using System.ClientModel;
using TechoramaOpenAI.Models;

namespace TechoramaOpenAI.Services;

public class OpenAIService(SecretClient secretClient, IOptions<OpenAISettings> options)
{
    private readonly OpenAISettings _settings = options.Value;

#pragma warning disable OPENAI001
    public async Task<string> UseResponsesAsync(string prompt)
    {
        try
        {
            string? openAiApiKey = await GetOpenAIApiKey();
            if (openAiApiKey == null) return "Error: OpenAI API key not configured";

            OpenAIClient openAIClient = new(new ApiKeyCredential(openAiApiKey));
            OpenAIResponseClient responseClient = openAIClient.GetOpenAIResponseClient(_settings.ModelName);
            OpenAIResponse response = await responseClient.CreateResponseAsync(prompt);

            return response.GetOutputText();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    public async Task<string> UseResponsesWithMcpAsync(string prompt)
    {
        string? openAiApiKey = await GetOpenAIApiKey();
        if (openAiApiKey == null) return "Error: OpenAI API key not configured";

        string? stripeApiKey = await GetStripeApiKey();
        if (stripeApiKey == null) return "Error: Stripe API key not configured";

        try
        {
            OpenAIResponseClient client = new(_settings.ModelName, openAiApiKey);

            ResponseCreationOptions options = new()
            {
                Tools =
                {
                    new McpTool(serverLabel: "stripe", serverUri: new Uri("https://mcp.stripe.com"))
                    {
                        ServerDescription = "MCP server docs at https://docs.stripe.com/mcp",
                        AuthorizationToken = stripeApiKey,
                        ToolCallApprovalPolicy = new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval),
                        AllowedTools = new McpToolFilter
                        {
                            // See available tools at https://docs.stripe.com/mcp#tools
                            ToolNames =
                            {
                                "list_products",
                                "list_prices",
                                "create_payment_link",
                            },
                            // When true, only allow read-only tools (those annotated with `readOnlyHint`)
                            //IsReadOnly = true,
                        },
                    },
                    new McpTool(serverLabel: "currency-conversion", serverUri: new Uri("https://currency-mcp.wesbos.com/mcp"))
                    {
                        ServerDescription = "MCP server docs at https://mcpservers.org/servers/wesbos/currency-converesion-mcp",
                        ToolCallApprovalPolicy = new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval),
                        AllowedTools = new McpToolFilter
                        {
                            // See available tools at https://github.com/wesbos/currency-conversion-mcp?tab=readme-ov-file#available-tools
                            ToolNames =
                            {
                                "convert_currency",
                            }
                        },
                    },
                }
            };

            OpenAIResponse response = await client.CreateResponseAsync(prompt, options);
            string output = response.GetOutputText();

            foreach (ResponseItem responseItem in response.OutputItems)
            {
                if (responseItem is McpToolDefinitionListItem listItem)
                {
                    if (!_settings.McpToolsListed.ContainsKey(listItem.ServerLabel))
                    {
                        _settings.McpToolsListed[listItem.ServerLabel] = new List<McpToolInfo>();
                    }

                    foreach (McpToolDefinition tool in listItem.ToolDefinitions)
                    {
                        _settings.McpToolsListed[listItem.ServerLabel].Add(new McpToolInfo
                        {
                            Name = tool.Name,
                            Annotations = tool.Annotations.ToString(),
                        });
                    }
                }
                else if (responseItem is McpToolCallItem callItem)
                {
                    _settings.McpToolsUsed.Add($"{callItem.ServerLabel}'s {callItem.ToolName}");
                }
            }

            return string.IsNullOrEmpty(output) ? "Response was empty or null" : output;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }
#pragma warning restore OPENAI001

    private async Task<string?> GetOpenAIApiKey()
    {
        KeyVaultSecret secret = await secretClient.GetSecretAsync("OPENAI-API-KEY");
        return secret?.Value;
    }

    private async Task<string?> GetStripeApiKey()
    {
        KeyVaultSecret secret = await secretClient.GetSecretAsync("STRIPE-OAUTH-ACCESS-TOKEN");
        return secret?.Value;
    }
}
