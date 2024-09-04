using Claudia;
using duetGPT.Components;
using duetGPT.Components.Account;
using duetGPT.Data;
using duetGPT.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDevExpressBlazor(options => {
    options.BootstrapVersion = DevExpress.Blazor.BootstrapVersion.v5;
    options.SizeMode = DevExpress.Blazor.SizeMode.Medium;
});

// Configure the maximum request body size to 50 MB
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 50 * 1024 * 1024; // 50 MB
});

// Add PostgreSQL DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
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
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
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
    if (file != null)
    {
        var result = await fileUploadService.UploadFile(file);
        return result ? Results.Ok("File uploaded successfully") : Results.BadRequest("File upload failed");
    }
    return Results.BadRequest("No file was uploaded");
});

// Apply database migrations
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();
        Console.WriteLine("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while applying database migrations.");
    }
}

app.Run();
