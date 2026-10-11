using BackendTts.Endpoints;
using BackendTts.Models;
using BackendTts.Services;

var builder = WebApplication.CreateBuilder(args);

// Konfigurasi: appsettings.json (section "Kiosk") → environment → command line.
var options = builder.Configuration.GetSection("Kiosk").Get<KioskOptions>() ?? new KioskOptions();
KioskOptions.ApplyCommandLine(builder.Configuration, options);
options.Validate();

// --host/--port selalu menang atas ASPNETCORE_URLS (mis. dari launchSettings.json),
// kecuali --urls diberikan eksplisit di command line.
var explicitUrls = args.Any(a => a.StartsWith("--urls", StringComparison.OrdinalIgnoreCase));
if (!explicitUrls)
    builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<ITtsEngine, EdgeTtsEngine>();
builder.Services.AddSingleton<AudioPlayer>();
builder.Services.AddSingleton<SpeechService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SpeechService>());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapKioskEndpoints();

app.Logger.LogInformation(
    "backend-tts siap — engine={Engine}, voice={Voice}, listen=http://{Host}:{Port}",
    options.Engine,
    options.Voice,
    options.Host,
    options.Port);

app.Run();
