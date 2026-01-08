using duetGPT.Components;
using duetGPT.Components.Account;
using duetGPT.Data;
using duetGPT.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();
builder.Services.AddSerilog();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDevExpressBlazor(options =>
{
    options.SizeMode = DevExpress.Blazor.SizeMode.Medium;
});

builder.Services.AddDevExpressServerSideBlazorPdfViewer();

// Add DevExpress AI Integration services for DxAIChat
builder.Services.AddDevExpressAI();

// Configure the maximum request body size to 50 MB
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 50 * 1024 * 1024; // 50 MB
});

// Add PostgreSQL DbContext Factory
builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Identity services with custom authentication configuration
builder.Services.AddIdentity<ApplicationUser, IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// Add UserManager and SignInManager explicitly
builder.Services.AddScoped<UserManager<ApplicationUser>>();
builder.Services.AddScoped<SignInManager<ApplicationUser>>();

// Add NoOpEmailSender
builder.Services.AddScoped<IEmailSender<ApplicationUser>, NoOpEmailSender>();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider<ApplicationUser>>();

// Add authorization services
builder.Services.AddAuthorization();

// Add AnthropicService
builder.Services.AddSingleton<AnthropicService>();
// Add OpenAIService
builder.Services.AddSingleton<OpenAIService>();
// Add ToolsService
//builder.Services.AddSingleton<ToolsService>();

// Add KnowledgeService
builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();

// Add new chat services
builder.Services.AddScoped<IChatMessageService, ChatMessageService>();
builder.Services.AddScoped<IThreadService, ThreadService>();
builder.Services.AddScoped<IImageService, ImageService>();
builder.Services.AddScoped<IThreadSummarizationService, ThreadSummarizationService>();

// Add chat context service for sharing state between UI and IChatClient adapter
builder.Services.AddScoped<IChatContextService, ChatContextService>();

// Register IChatClient adapter for DevExpress DxAIChat integration
// DxAIChat automatically discovers IChatClient through standard DI (no keyed services needed)
builder.Services.AddScoped<AnthropicChatClientAdapter>();
builder.Services.AddScoped<Microsoft.Extensions.AI.IChatClient>(sp =>
    sp.GetRequiredService<AnthropicChatClientAdapter>());

// Add FileUploadService
builder.Services.AddScoped<FileUploadService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add Identity endpoints
app.MapAdditionalIdentityEndpoints();

// Add file upload endpoint
app.MapPost("/api/UploadValidation/Upload", async (HttpRequest request, FileUploadService fileUploadService) =>
{
    var file = request.Form.Files.FirstOrDefault();
    var userId = request.Query["userId"].ToString();
    if (file != null && !string.IsNullOrEmpty(userId))
    {
        var result = await fileUploadService.UploadFile(file, userId);
        return result ? Results.Ok("File uploaded successfully") : Results.BadRequest("File upload failed");
    }
    return Results.BadRequest("No file was uploaded or user ID was not provided");
});

// Apply database migrations
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();
        Log.Information("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        Log.Error(ex, "An error occurred while applying database migrations.");
    }

    // Verify IChatClient registration for DxAIChat integration
    try
    {
        var chatClient = services.GetRequiredService<Microsoft.Extensions.AI.IChatClient>();
        Log.Warning("=== IChatClient VERIFIED === Type: {Type}", chatClient.GetType().FullName);
    }
    catch (Exception ex)
    {
        Log.Error(ex, "CRITICAL: IChatClient not registered properly!");
    }
}

app.Run();
