
using duetGPT.Components;
using Claudia;
using Microsoft.Extensions.FileProviders;


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
    
    var apiKey = configuration["Anthropic:ApiKey"];
    if (apiKey == null)
    {
        throw new InvalidOperationException("API key for Anthropic is not configured.");
    }

    var anthropic = new Anthropic
    {
        ApiKey = apiKey    };
    return anthropic;
});
var app = builder.Build();

// Configure the HTTP request pipeline.
if(!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseHttpsRedirection();

#if DEBUG
app.UseStaticFiles();
#else
app.UseStaticFiles(new StaticFileOptions
{
    RequestPath = "/wwwroot"
});
#endif
app.UseAntiforgery();

app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();

app.Run();