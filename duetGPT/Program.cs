using duetGPT.Components;
using duetGPT.Components.Account;
using duetGPT.Data;
using duetGPT.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using System.Text;

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

// Add JWT Authentication for API endpoints
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var secretKey = jwtSettings["SecretKey"] ?? "YourSuperSecretKeyThatIsAtLeast32CharactersLong!";
builder.Services.AddAuthentication()
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"] ?? "duetGPT",
        ValidAudience = jwtSettings["Audience"] ?? "duetGPT-users",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };
});

// Add authorization services with policies for different schemes
builder.Services.AddAuthorization(options =>
{
    // Default policy accepts both cookies (for Blazor) and JWT (for API)
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();
});

// Add Controllers for API endpoints
builder.Services.AddControllers();

// Add AnthropicService
builder.Services.AddSingleton<AnthropicService>();
// Add OpenAIService
builder.Services.AddSingleton<OpenAIService>();
// Add ToolsService
//builder.Services.AddSingleton<ToolsService>();

// Add KnowledgeService
builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();

// Add DocumentProcessingService
builder.Services.AddScoped<DocumentProcessingService>();

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

// Add CORS for Next.js frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowNextJs", policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

Console.WriteLine("Building application...");
var app = builder.Build();
Console.WriteLine("Application built successfully");

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Only use HTTPS redirection in non-Testing environments
if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
Console.WriteLine("Configuring middleware...");
app.UseStaticFiles();

// Enable CORS for Next.js frontend
app.UseCors("AllowNextJs");

app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

Console.WriteLine("Mapping components and endpoints...");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add Identity endpoints
app.MapAdditionalIdentityEndpoints();

// Map API Controllers
app.MapControllers();

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

// Skip database migrations for now - run manually with: dotnet ef database update
Log.Information("=== Skipping automatic database migrations ===");
Log.Information("=== Application starting... ===");

app.Run();
