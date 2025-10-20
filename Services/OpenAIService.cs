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
        string? openAiApiKey = await GetOpenAIApiKey();
        if (openAiApiKey == null) return "Error: OpenAI API key not configured";

        try
        {
            OpenAIClient openAIClient = new(new ApiKeyCredential(openAiApiKey));
            OpenAIResponseClient responseClient = openAIClient.GetOpenAIResponseClient(_settings.ModelName);
            OpenAIResponse response;

            if (_settings.ModelName == "gpt-5")
            {
                // The REST API spec hasn't been updated to include gpt-5 properties.
                // As a workaround, force additional members into the options properties bag to tune perf.
                // For more context, see https://github.com/openai/openai-dotnet/issues/593 and
                // https://platform.openai.com/docs/api-reference/responses/create.
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
        string?[] results = await Task.WhenAll(GetOpenAIApiKey(), GetStripeApiKey());
        var (openAIApiKey, stripeApiKey) = (results[0], results[1]);
        
        if (openAIApiKey == null) return "Error: OpenAI API key not configured";
        if (stripeApiKey == null) return "Error: Stripe API key not configured";

        try
        {
            OpenAIResponseClient client = new(_settings.ModelName, openAIApiKey);
            ResponseCreationOptions options = CreateMcpOptions(stripeApiKey);
            
            OpenAIResponse response = await client.CreateResponseAsync(prompt, options);
            response = await ProcessResponseWithApprovalsAsync(client, response, options);
            
            string output = response.GetOutputText();
            return string.IsNullOrEmpty(output) ? "Response was empty or null" : output;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static ResponseCreationOptions CreateMcpOptions(string stripeApiKey)
    {
        return new ResponseCreationOptions
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
                        ToolNames =
                        {
                            "list_products",
                            "list_prices",
                            "create_payment_link",
                        },
                    },
                },
                new McpTool(serverLabel: "currency-conversion", serverUri: new Uri("https://currency-mcp.wesbos.com/mcp"))
                {
                    ServerDescription = "MCP server docs at https://mcpservers.org/servers/wesbos/currency-converesion-mcp",
                    ToolCallApprovalPolicy = new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.AlwaysRequireApproval),
                    AllowedTools = new McpToolFilter
                    {
                        ToolNames =
                        {
                            "convert_currency",
                        },
                    },
                },
            }
        };
    }

    private async Task<OpenAIResponse> ProcessResponseWithApprovalsAsync(
        OpenAIResponseClient client, 
        OpenAIResponse response, 
        ResponseCreationOptions options)
    {
        while (true)
        {
            var (conversationItems, hasApprovalRequests) = await ProcessResponseItemsAsync(response.OutputItems);
            
            if (!hasApprovalRequests)
            {
                return response;
            }
            
            response = await client.CreateResponseAsync(conversationItems, options);
        }
    }

    private async Task<(List<ResponseItem> Items, bool HasApprovalRequests)> ProcessResponseItemsAsync(
        IList<ResponseItem> outputItems)
    {
        List<ResponseItem> conversationItems = new(outputItems.Count);
        bool hasApprovalRequests = false;

        foreach (ResponseItem responseItem in outputItems)
        {
            switch (responseItem)
            {
                case McpToolCallApprovalRequestItem requestItem:
                    hasApprovalRequests = true;
                    await HandleApprovalRequestAsync(requestItem, conversationItems);
                    break;
                case McpToolDefinitionListItem listItem:
                    HandleToolDefinitionList(listItem);
                    conversationItems.Add(responseItem);
                    break;
                case McpToolCallItem callItem:
                    HandleToolCall(callItem);
                    conversationItems.Add(responseItem);
                    break;
                default:
                    conversationItems.Add(responseItem);
                    break;
            }
        }

        return (conversationItems, hasApprovalRequests);
    }

    private async Task HandleApprovalRequestAsync(
        McpToolCallApprovalRequestItem requestItem,
        List<ResponseItem> conversationItems)
    {
        bool approved = await _toastService.ShowApprovalToastAsync(
            "MCP Tool Approval Required",
            $"The AI wants to use the '{requestItem.ToolName}' tool from '{requestItem.ServerLabel}'. Do you approve?");

        conversationItems.Add(requestItem);
        conversationItems.Add(ResponseItem.CreateMcpApprovalResponseItem(requestItem.Id, approved));
    }

    private void HandleToolDefinitionList(McpToolDefinitionListItem listItem)
    {
        if (!_settings.McpToolsListed.TryGetValue(listItem.ServerLabel, out var tools))
        {
            tools = new List<McpToolInfo>();
            _settings.McpToolsListed[listItem.ServerLabel] = tools;
        }

        foreach (McpToolDefinition tool in listItem.ToolDefinitions)
        {
            if (!tools.Any(t => t.Name == tool.Name))
            {
                tools.Add(new McpToolInfo
                {
                    Name = tool.Name,
                    Annotations = tool.Annotations.ToString(),
                });
            }
        }
    }

    private void HandleToolCall(McpToolCallItem callItem) =>
        _settings.McpToolsUsed.Add($"{callItem.ServerLabel}.{callItem.ToolName}");
#pragma warning restore OPENAI001

    private async Task<string?> GetOpenAIApiKey()
    {
        const string OpenAIKeyCacheKey = "OPENAI-API-KEY";

        return await _cache.GetOrCreateAsync(OpenAIKeyCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7);
            KeyVaultSecret secret = await secretClient.GetSecretAsync(OpenAIKeyCacheKey).ConfigureAwait(false);
            return secret?.Value;
        });
    }

    private async Task<string?> GetStripeApiKey()
    {
        const string StripeKeyCacheKey = "STRIPE-OAUTH-ACCESS-TOKEN";

        return await _cache.GetOrCreateAsync(StripeKeyCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7);
            KeyVaultSecret secret = await secretClient.GetSecretAsync(StripeKeyCacheKey).ConfigureAwait(false);
            return secret?.Value;
        });
    }
}
