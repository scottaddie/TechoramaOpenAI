using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Responses;
using System.ClientModel;
using System.ClientModel.Primitives;
using TechoramaOpenAI.Models;

namespace TechoramaOpenAI.Services;

public class OpenAIService(
    SecretClient secretClient,
    IOptions<OpenAISettings> options,
    IMemoryCache cache,
    ToastService toastService)
{
    private readonly OpenAISettings _settings = options.Value;
    private readonly IMemoryCache _cache = cache;
    private readonly ToastService _toastService = toastService;

#pragma warning disable OPENAI001
    public async Task<string> UseResponsesAsync(string prompt)
    {
        try
        {
            string? openAiApiKey = await GetOpenAIApiKey();
            if (openAiApiKey == null) return "Error: OpenAI API key not configured";

            OpenAIClient openAIClient = new(new ApiKeyCredential(openAiApiKey));
            OpenAIResponseClient responseClient = openAIClient.GetOpenAIResponseClient(_settings.ModelName);
            OpenAIResponse response;

            if (_settings.ModelName == "gpt-5")
            {
                // The REST API spec hasn't been updated to include gpt-5 properties.
                // As a workaround, force additional members into the options properties bag.
                // See https://github.com/openai/openai-dotnet/issues/593.
                ResponseCreationOptions? options = ((IJsonModel<ResponseCreationOptions>)new ResponseCreationOptions())
                    .Create(BinaryData.FromObjectAsJson(new
                    {
                        reasoning = new { effort = "minimal" },
                        text = new { verbosity = "low" }
                    }), ModelReaderWriterOptions.Json);
                response = await responseClient.CreateResponseAsync(prompt, options);
            }
            else
            {
                response = await responseClient.CreateResponseAsync(prompt);
            }

            return response.GetOutputText();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    public async Task<string> UseResponsesWithMcpAsync(string prompt)
    {
        // Fetch both keys in parallel
        var (openAIApiKey, stripeApiKey) = await Task.WhenAll(
            GetOpenAIApiKey(),
            GetStripeApiKey()
        ).ContinueWith(t => (t.Result[0], t.Result[1]));

        if (openAIApiKey == null) return "Error: OpenAI API key not configured";
        if (stripeApiKey == null) return "Error: Stripe API key not configured";

        try
        {
            OpenAIResponseClient client = new(_settings.ModelName, openAIApiKey);

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
                        ToolCallApprovalPolicy = new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.AlwaysRequireApproval),
                        AllowedTools = new McpToolFilter
                        {
                            // See available tools at https://github.com/wesbos/currency-conversion-mcp?tab=readme-ov-file#available-tools
                            ToolNames =
                            {
                                "convert_currency",
                            },
                        },
                    },
                }
            };

            OpenAIResponse response = await client.CreateResponseAsync(prompt, options);

            // Process approval requests in a loop until we get a final response
            bool hasApprovalRequests = true;
            while (hasApprovalRequests)
            {
                hasApprovalRequests = false;
                List<ResponseItem> conversationItems = new();

                foreach (ResponseItem responseItem in response.OutputItems)
                {
                    if (responseItem is McpToolCallApprovalRequestItem requestItem)
                    {
                        hasApprovalRequests = true;

                        bool approved = await _toastService.ShowApprovalToastAsync(
                            "MCP Tool Approval Required",
                            $"The AI wants to use the '{requestItem.ToolName}' tool from '{requestItem.ServerLabel}'. Do you approve?");

                        // Include the original approval request item
                        conversationItems.Add(responseItem);
                        
                        // Add the approval response item immediately after
                        conversationItems.Add(
                            ResponseItem.CreateMcpApprovalResponseItem(responseItem.Id, approved));
                    }
                    else if (responseItem is McpToolDefinitionListItem listItem)
                    {
                        if (!_settings.McpToolsListed.ContainsKey(listItem.ServerLabel))
                        {
                            _settings.McpToolsListed[listItem.ServerLabel] = new List<McpToolInfo>();
                        }

                        foreach (McpToolDefinition tool in listItem.ToolDefinitions)
                        {
                            // Check if the tool is already in the list to avoid duplicates
                            if (!_settings.McpToolsListed[listItem.ServerLabel].Any(t => t.Name == tool.Name))
                            {
                                _settings.McpToolsListed[listItem.ServerLabel].Add(new McpToolInfo
                                {
                                    Name = tool.Name,
                                    Annotations = tool.Annotations.ToString(),
                                });
                            }
                        }
                        
                        // Add the original item to continue the conversation
                        conversationItems.Add(responseItem);
                    }
                    else if (responseItem is McpToolCallItem callItem)
                    {
                        _settings.McpToolsUsed.Add($"{callItem.ServerLabel}.{callItem.ToolName}");
                        
                        // Add the original item to continue the conversation
                        conversationItems.Add(responseItem);
                    }
                    else
                    {
                        // Add all other items to continue the conversation
                        conversationItems.Add(responseItem);
                    }
                }

                // If there were approval requests, send the conversation items (including approvals) and get the next response
                if (hasApprovalRequests)
                {
                    response = await client.CreateResponseAsync(conversationItems, options);
                }
            }

            string output = response.GetOutputText();
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
        const string OpenAIKeyCacheKey = "OPENAI-API-KEY";

        return await _cache.GetOrCreateAsync(OpenAIKeyCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7);
            KeyVaultSecret secret = await secretClient.GetSecretAsync(OpenAIKeyCacheKey);
            return secret?.Value;
        });
    }

    private async Task<string?> GetStripeApiKey()
    {
        const string StripeKeyCacheKey = "STRIPE-OAUTH-ACCESS-TOKEN";

        return await _cache.GetOrCreateAsync(StripeKeyCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7);
            KeyVaultSecret secret = await secretClient.GetSecretAsync(StripeKeyCacheKey);
            return secret?.Value;
        });
    }
}
