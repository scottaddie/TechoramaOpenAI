using Azure.Identity;
using OpenAI;
using OpenAI.Responses;
using System.ClientModel.Primitives;

#pragma warning disable OPENAI001

// DEMO 1
await UseAzureOpenAiResponses();

// DEMO 2
await UseOpenAiResponsesWithRemoteMcpTool();

static async Task UseAzureOpenAiResponses()
{
    OpenAIResponseClient client = GetAzureOpenAiResponseClient(useEntra: false);
    OpenAIResponse response = await client.CreateResponseAsync("Say 'This is a test.'");

    Console.WriteLine(response.GetOutputText());
}

static OpenAIResponseClient GetAzureOpenAiResponseClient(bool useEntra)
{
    const string azureOpenAiEndpoint = "https://openai-jc4hwqn6g2r7g.openai.azure.com";

    if (useEntra)
    {
        VisualStudioCredentialOptions credentialOptions = new()
        {
            TenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID"),
        };
        VisualStudioCredential credential = new(credentialOptions);
        BearerTokenPolicy tokenPolicy = new(
            tokenProvider: credential,
            scope: "https://cognitiveservices.azure.com/.default");

        OpenAIClientOptions clientOptions = new()
        {
            Endpoint = new Uri($"{azureOpenAiEndpoint}/openai/v1/"),
        };

        return new(
            model: "codex-mini-2",
            authenticationPolicy: tokenPolicy,
            options: clientOptions);
    }

    return new(
        model: "codex-mini-2",
        apiKey: GetOpenAiApiKey());
}

static async Task UseOpenAiResponsesWithRemoteMcpTool()
{
    string? openAiApiKey = GetOpenAiApiKey();
    string? stripeApiKey = GetStripeApiKey();

    try
    {
        OpenAIResponseClient client = new(model: "gpt-5", apiKey: openAiApiKey);

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

        Console.WriteLine("Sending request to OpenAI with Stripe MCP tools...");
        OpenAIResponse response = await client.CreateResponseAsync("List my Stripe products", options);

        string output = response.GetOutputText();

        if (string.IsNullOrEmpty(output))
            Console.WriteLine("Response was empty or null");
        else
            Console.WriteLine($"Response: {output}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unexpected error: {ex.Message}");
        Console.WriteLine($"Exception type: {ex.GetType().Name}");
        Console.WriteLine($"Stack trace: {ex.StackTrace}");
    }
}

#pragma warning restore OPENAI001

static string? GetOpenAiApiKey()
{
    string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

    if (string.IsNullOrEmpty(apiKey))
    {
        Console.WriteLine("Error: OPENAI_API_KEY environment variable is not set.");
        return null;
    }

    return apiKey;
}

static string? GetStripeApiKey()
{
    string? authToken = Environment.GetEnvironmentVariable("STRIPE_OAUTH_ACCESS_TOKEN");

    if (string.IsNullOrEmpty(authToken))
    {
        Console.WriteLine("Error: STRIPE_OAUTH_ACCESS_TOKEN environment variable is not set.");
        return null;
    }

    return authToken;
}
