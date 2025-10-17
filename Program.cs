using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Azure;
using TechoramaOpenAI.Models;
using TechoramaOpenAI.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMemoryCache();

builder.Services.AddSingleton<ToastService>();

builder.Services.AddSingleton<TokenCredential>(_ =>
    new DefaultAzureCredential(DefaultAzureCredential.DefaultEnvironmentVariableName));

builder.Services.Configure<OpenAISettings>(builder.Configuration.GetSection("OpenAI"));
builder.Services.AddSingleton<OpenAIService>();

builder.Services.Configure<AzureOpenAISettings>(builder.Configuration.GetSection("Azure:OpenAI"));
builder.Services.AddSingleton<AzureOpenAIService>();

builder.Services.AddAzureClients(configureClients: c =>
{
    IConfigurationSection keyVaultConfig = builder.Configuration.GetSection("Azure:KeyVault");
    c.AddSecretClient(keyVaultConfig);

    c.UseCredential(serviceProvider => serviceProvider.GetRequiredService<TokenCredential>());
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");
app.Run();
