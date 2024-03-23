using duetGPT.Data;
using duetGPT.Components;
using Claudia;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddDevExpressBlazor(options => {
    options.BootstrapVersion = DevExpress.Blazor.BootstrapVersion.v5;
    options.SizeMode = DevExpress.Blazor.SizeMode.Medium;
});
builder.Services.AddSingleton<WeatherForecastService>();
// Add Anthropic Client
builder.Services.AddSingleton<Anthropic>(provider =>
{
    var anthropic = new Anthropic
    {
        ApiKey = "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"
    };
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

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();