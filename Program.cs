using System.Globalization;
using Microsoft.AspNetCore.HttpOverrides;
using MudBlazor.Services;
using WMS.Components;
using WMS.Services;

CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("it-IT");
CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("it-IT");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents(options =>
    {
        options.DetailedErrors = true;
    });

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = MudBlazor.Defaults.Classes.Position.BottomCenter;
    config.SnackbarConfiguration.ShowTransitionDuration = 200;
    config.SnackbarConfiguration.HideTransitionDuration = 200;
    config.SnackbarConfiguration.VisibleStateDuration = 2500;
});

// Scoped = per circuito SignalR (= per sessione utente in Blazor Server)
builder.Services.AddScoped<SessionService>();

// Singleton = dati condivisi tra tutti gli utenti demo (stato magazzino mock)
builder.Services.AddSingleton<MockWmsService>();

// Singleton = accesso al DB ERP reale (connessioni aperte/chiuse per chiamata)
builder.Services.AddSingleton<ErpService>();

// Singleton = accesso al DB Logic (carrello persistente, configurazione WMS)
builder.Services.AddSingleton<LogicService>();

// Singleton = servizio stampa (Intesi Printer Manager o legacy Crystal)
builder.Services.AddHttpClient<PrintService>();

// IIS / reverse proxy support
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Crea DB Logic e tabelle WMS se non esistono (idempotente)
await app.Services.GetRequiredService<LogicService>().EnsureSchemaAsync();

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// NOTA: UseHttpsRedirection rimosso — la terminazione HTTPS è gestita da IIS
// app.UseHttpsRedirection();

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
