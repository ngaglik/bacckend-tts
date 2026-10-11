using System.Security.Cryptography;
using System.Text;
using BackendTts.Helpers;
using BackendTts.Models;
using BackendTts.Services;

namespace BackendTts.Endpoints;

/// <summary>
/// Endpoint TTS kiosk. Kontrak dipakai oleh <c>erp/backend</c>
/// (<c>KioskAnnouncer</c>) dan halaman Uji Suara <c>erp/antrian</c>:
/// <list type="bullet">
///   <item><c>GET /call?number=&amp;counter=&amp;service=&amp;customer=&amp;repeat=&amp;phase=&amp;template=</c> — panggil nomor antrian.
///         <c>template</c> opsional (mis. dari halaman Pengaturan antrian) menimpa
///         template bawaan (<c>--call-template</c>/<c>--serving-template</c>/<c>--done-template</c>).</item>
///   <item><c>GET /speak?text=&amp;repeat=</c> — bacakan teks bebas.</item>
///   <item><c>GET /stop</c> — hentikan bacaan &amp; kosongkan antrian.</item>
/// </list>
/// Bila <c>Kiosk:Token</c> diisi, semua endpoint aksi wajib menyertakan
/// <c>?token=</c> (atau header <c>X-Kiosk-Token</c>).
/// </summary>
public static class KioskEndpoints
{
    public static void MapKioskEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(string.Empty).WithTags("Kiosk");

        // ── Cek kesehatan / info konfigurasi ─────────────────────────────
        group.MapGet("/health", (KioskOptions options, SpeechService speech) =>
        {
            return Results.Ok(new
            {
                ok = true,
                status = "ok",
                engine = options.Engine,
                voice = options.Voice,
                rate = options.Rate,
                speaking = speech.IsSpeaking,
                currentText = speech.CurrentText,
                queued = speech.QueuedCount,
                player = speech.PlayerName,
                authRequired = !string.IsNullOrWhiteSpace(options.Token),
                serverTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            });
        })
        .WithName("KioskHealth");

        // ── Panggil nomor antrian ────────────────────────────────────────
        group.MapGet("/call", (
            HttpContext context,
            KioskOptions options,
            SpeechService speech,
            string? number,
            string? counter,
            string? service,
            string? customer,
            int? repeat,
            string? phase,
            string? template) =>
        {
            if (!IsAuthorized(context, options)) return Unauthorized();

            if (string.IsNullOrWhiteSpace(number))
                return Results.BadRequest(new { ok = false, message = "Parameter 'number' wajib diisi." });

            var normalizedPhase = NormalizePhase(phase);
            var repeatCount = ClampRepeat(repeat, options);

            // Template dari pengaturan antrian (query) menang atas template
            // bawaan yang diatur lewat konfigurasi service.
            var resolvedTemplate = string.IsNullOrWhiteSpace(template)
                ? ResolvePhaseTemplate(options, normalizedPhase)
                : template!;

            var text = SpeechText.BuildCallText(
                options,
                number.Trim(),
                counter,
                service,
                resolvedTemplate,
                customer);
            var result = speech.Enqueue(SpeechSource.Call, text, repeatCount);

            return result.Ok
                ? Results.Ok(new
                {
                    ok = true,
                    message = result.Message,
                    text,
                    phase = normalizedPhase,
                    repeat = repeatCount,
                    queued = result.Queued,
                })
                : Results.Json(new { ok = false, message = result.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .WithName("KioskCall");

        // ── Bacakan teks bebas (halaman Uji Suara) ───────────────────────
        group.MapGet("/speak", (
            HttpContext context,
            KioskOptions options,
            SpeechService speech,
            string? text,
            int? repeat) =>
        {
            if (!IsAuthorized(context, options)) return Unauthorized();

            if (string.IsNullOrWhiteSpace(text))
                return Results.BadRequest(new { ok = false, message = "Parameter 'text' wajib diisi." });

            var trimmed = text.Trim();
            if (trimmed.Length > options.MaxTextLength)
            {
                return Results.BadRequest(new
                {
                    ok = false,
                    message = $"Teks terlalu panjang ({trimmed.Length} karakter, maksimum {options.MaxTextLength}).",
                });
            }

            var repeatCount = ClampRepeat(repeat, options);
            var result = speech.Enqueue(SpeechSource.Speak, trimmed, repeatCount);

            return result.Ok
                ? Results.Ok(new
                {
                    ok = true,
                    message = result.Message,
                    text = trimmed,
                    repeat = repeatCount,
                    length = trimmed.Length,
                    queued = result.Queued,
                })
                : Results.Json(new { ok = false, message = result.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
        })
        .WithName("KioskSpeak");

        // ── Hentikan bacaan ──────────────────────────────────────────────
        group.MapGet("/stop", (HttpContext context, KioskOptions options, SpeechService speech) =>
        {
            if (!IsAuthorized(context, options)) return Unauthorized();

            var result = speech.Stop();
            return Results.Ok(new { ok = result.Ok, message = result.Message, drained = result.Drained });
        })
        .WithName("KioskStop");

        // ── Daftar voice edge-tts ────────────────────────────────────────
        group.MapGet("/voices", async (
            HttpContext context,
            KioskOptions options,
            ITtsEngine engine,
            CancellationToken cancellationToken) =>
        {
            if (!IsAuthorized(context, options)) return Unauthorized();

            try
            {
                var voices = await engine.ListVoicesAsync(cancellationToken);
                return Results.Ok(new
                {
                    ok = true,
                    engine = engine.Name,
                    voice = engine.Voice,
                    count = voices.Count,
                    voices,
                });
            }
            catch (Exception ex)
            {
                return Results.Json(
                    new { ok = false, message = ex.Message },
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .WithName("KioskVoiceList");
    }

    private static int ClampRepeat(int? repeat, KioskOptions options)
    {
        var value = repeat ?? options.Repeats;
        return Math.Clamp(value, 1, options.MaxRepeats);
    }

    /// <summary>Fase panggilan: <c>call</c> (awal), <c>serving</c> (sedang diproses), <c>done</c> (selesai).</summary>
    private static string NormalizePhase(string? phase) =>
        (phase ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "serving" or "proses" or "diproses" => "serving",
            "done" or "selesai" or "finished" => "done",
            _ => "call",
        };

    private static string ResolvePhaseTemplate(KioskOptions options, string phase) => phase switch
    {
        "serving" => options.ServingTemplate,
        "done" => options.DoneTemplate,
        _ => options.CallTemplate,
    };

    private static IResult Unauthorized() =>
        Results.Json(
            new { ok = false, message = "Token tidak valid atau tidak disertakan." },
            statusCode: StatusCodes.Status401Unauthorized);

    private static bool IsAuthorized(HttpContext context, KioskOptions options)
    {
        var expected = options.Token;
        if (string.IsNullOrWhiteSpace(expected)) return true;

        var provided = context.Request.Query["token"].ToString();
        if (string.IsNullOrWhiteSpace(provided))
            provided = context.Request.Headers["X-Kiosk-Token"].ToString();

        if (string.IsNullOrWhiteSpace(provided)) return false;

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);
        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
