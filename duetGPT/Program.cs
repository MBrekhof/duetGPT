using duetGPT.Components;
using Claudia;
using duetGPT.Services;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDevExpressBlazor(options => {
    options.BootstrapVersion = DevExpress.Blazor.BootstrapVersion.v5;
    options.SizeMode = DevExpress.Blazor.SizeMode.Medium;
});

// Add Anthropic Client
builder.Services.AddSingleton<Anthropic>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var logger = provider.GetRequiredService<ILogger<Program>>();
    
    var apiKey = configuration["Anthropic:ApiKey"];
    if (string.IsNullOrEmpty(apiKey))
    {
        logger.LogError("API key for Anthropic is not configured.");
        return null; // Return null instead of throwing an exception
    }

    try
    {
        var anthropic = new Anthropic
        {
            ApiKey = apiKey
        };
        logger.LogInformation("Anthropic client initialized successfully.");
        return anthropic;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize Anthropic client.");
        return null;
    }
});

// Add a hosted service to check Anthropic client status
builder.Services.AddHostedService<AnthropicHealthCheckService>();

// Add FileUploadService
builder.Services.AddScoped<FileUploadService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if(!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add file upload endpoint
app.MapPost("/api/UploadValidation/Upload", async (HttpRequest request, FileUploadService fileUploadService) =>
{
    var file = request.Form.Files.FirstOrDefault();
    if (file != null)
    {
        var result = await fileUploadService.UploadFile(file);
        return result ? Results.Ok("File uploaded successfully") : Results.BadRequest("File upload failed");
    }
    return Results.BadRequest("No file was uploaded");
});

app.Run();