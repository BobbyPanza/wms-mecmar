using System.Globalization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using MudBlazor.Services;
using WMS.Components;
using WMS.Models;
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

// Singleton = servizio liste di prelievo (Logic DB + ERP)
builder.Services.AddSingleton<PickListService>();

// Configurazione liste di prelievo (sezione "PickList" in appsettings.json)
builder.Services.Configure<PickListOptions>(
    builder.Configuration.GetSection("PickList"));

// Opzioni generali WMS (magazzini abilitati, ecc.)
builder.Services.Configure<WmsOptions>(
    builder.Configuration.GetSection("Wms"));

// Configurazione accettazione (sezione "Acceptance" in appsettings.json)
builder.Services.Configure<AcceptanceOptions>(
    builder.Configuration.GetSection("Acceptance"));

// Singleton = orchestrazione accettazione merce
builder.Services.AddSingleton<AcceptanceService>();

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
var contentTypes = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
contentTypes.Mappings[".cer"] = "application/x-x509-ca-cert";
contentTypes.Mappings[".webmanifest"] = "application/manifest+json";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypes });
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/api/thumbnail/{pacod}", (string pacod, IConfiguration config, HttpResponse response) =>
{
    // Blocca path traversal
    if (pacod.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        return Results.BadRequest();

    var basePath = config["ArticleThumbnails:UncPath"];
    if (string.IsNullOrEmpty(basePath)) return Results.NotFound();

    var filePath = Path.Combine(basePath, $"{pacod}.png");
    if (!File.Exists(filePath)) return Results.NotFound();

    response.Headers.CacheControl = "public, max-age=3600";
    return Results.File(filePath, "image/png");
});

app.MapGet("/api/acceptance-photo/{id:guid}", async (Guid id, IConfiguration config, LogicService logic, HttpResponse response) =>
{
    var photo = await logic.GetPhotoAsync(id);
    if (photo is null) return Results.NotFound();

    var storagePath = config["AcceptancePhotos:StoragePath"];
    if (string.IsNullOrEmpty(storagePath)) return Results.NotFound();

    var filePath = Path.Combine(storagePath, photo.FileName);
    if (!File.Exists(filePath)) return Results.NotFound();

    var contentType = Path.GetExtension(photo.FileName).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png"  => "image/png",
        ".webp" => "image/webp",
        _ => "image/jpeg"
    };
    response.Headers.CacheControl = "private, max-age=3600";
    return Results.File(filePath, contentType);
});

app.MapDelete("/api/acceptance-photo/{id:guid}", async (Guid id, IConfiguration config, LogicService logic) =>
{
    var photo = await logic.GetPhotoAsync(id);
    if (photo is null) return Results.NotFound();

    var storagePath = config["AcceptancePhotos:StoragePath"];
    if (!string.IsNullOrEmpty(storagePath))
    {
        var filePath = Path.Combine(storagePath, photo.FileName);
        if (File.Exists(filePath)) File.Delete(filePath);
    }
    await logic.DeletePhotoAsync(id);
    return Results.Ok();
});

app.Run();
