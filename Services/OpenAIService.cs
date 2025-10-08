using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Responses;
using System.ClientModel;
using System.ClientModel.Primitives;
using TechoramaOpenAI.Models;

#pragma warning disable OPENAI001

public class OpenAIService (SecretClient secretClient, IOptions<OpenAISettings> options)
{
    private readonly OpenAISettings _settings = options.Value;
    
    public async Task<string> UseOpenAiResponsesAsync(string prompt)
    {
        try
        {
            string? apiKey = await GetOpenAiApiKey();
            if (apiKey == null) return "Error: OpenAI API key not configured";

            OpenAIClient openAIClient = new(new ApiKeyCredential(apiKey));
            OpenAIResponseClient responseClient = openAIClient.GetOpenAIResponseClient("gpt-4");
            OpenAIResponse response = await responseClient.CreateResponseAsync(prompt);

            return response.GetOutputText();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    public async Task<string> UseAzureOpenAiResponsesAsync(string prompt, bool useEntraId = false)
    {
        try
        {
            OpenAIClient openAIClient = await GetAzureOpenAIClient(useEntraId);
            OpenAIResponseClient responseClient = openAIClient.GetOpenAIResponseClient(_settings.DeploymentName);
            //OpenAIResponseClient responseClient = openAIClient.GetOpenAIResponseClient("gpt-5-mini");
            OpenAIResponse response = await responseClient.CreateResponseAsync(prompt);

            return response.GetOutputText();
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    public async Task<string> UseOpenAiResponsesWithMcpAsync(string prompt)
    {
        string? openAiApiKey = await GetOpenAiApiKey();
        string? stripeApiKey = await GetStripeApiKey();

        if (openAiApiKey == null) return "Error: OpenAI API key not configured";
        if (stripeApiKey == null) return "Error: Stripe API key not configured";

        try
        {
            OpenAIResponseClient client = new(model: "gpt-4", apiKey: openAiApiKey);

            ResponseCreationOptions options = new()
            {
                Tools =
                {
                    new McpTool(serverLabel: "stripe", serverUri: new Uri("https://mcp.stripe.com"))
                    {
                        AuthorizationToken = stripeApiKey,
                        ToolCallApprovalPolicy = new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval),
                    },
                }
            };

            OpenAIResponse response = await client.CreateResponseAsync(prompt, options);
            string output = response.GetOutputText();

            return string.IsNullOrEmpty(output) ? "Response was empty or null" : output;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private async Task<OpenAIClient> GetAzureOpenAIClient(bool useEntraId)
    {
        OpenAIClient client = null!;

        OpenAIClientOptions clientOptions = new()
        {
            Endpoint = new Uri($"{_settings.Endpoint}/openai/v1/"),
        };

        if (useEntraId)
        {
            //TODO: find a way to pass this cred in from Program.cs so it's reused and uses token cache
            DefaultAzureCredential credential = new(DefaultAzureCredential.DefaultEnvironmentVariableName);
            BearerTokenPolicy tokenPolicy = new(credential, _settings.Scope);
            client = new(tokenPolicy, clientOptions);
        }
        else
        {
            string? apiKey = await GetAzureOpenAiApiKey() ?? throw new InvalidOperationException("Azure OpenAI API key not configured");
            client = new(new ApiKeyCredential(apiKey), clientOptions);
        }

        return client;
    }
#pragma warning restore OPENAI001

    private async Task<string?> GetAzureOpenAiApiKey()
    {
        KeyVaultSecret secret = await secretClient.GetSecretAsync("AZURE-OPENAI-API-KEY");
        return secret?.Value;
    }

    private async Task<string?> GetOpenAiApiKey()
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
