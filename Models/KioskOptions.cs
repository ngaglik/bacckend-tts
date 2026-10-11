using Microsoft.Extensions.Configuration;

namespace BackendTts.Models;

/// <summary>
/// Konfigurasi service TTS kiosk. Nilai dapat diisi dari
/// <c>appsettings.json</c> (section <c>Kiosk</c>), environment variable
/// (mis. <c>Kiosk__Voice</c>), atau argumen command line
/// (mis. <c>--engine edge --voice id-ID-GadisNeural</c>).
/// </summary>
public sealed class KioskOptions
{
    /// <summary>Engine TTS. Saat ini hanya <c>edge</c> (edge-tts).</summary>
    public string Engine { get; set; } = "edge";

    /// <summary>Nama voice edge-tts, mis. <c>id-ID-GadisNeural</c>.</summary>
    public string Voice { get; set; } = "id-ID-GadisNeural";

    /// <summary>Kecepatan bicara edge-tts, mis. <c>+0%</c>, <c>-10%</c>, <c>+20%</c>.</summary>
    public string Rate { get; set; } = "+0%";

    /// <summary>Volume edge-tts, mis. <c>+0%</c>.</summary>
    public string Volume { get; set; } = "+0%";

    /// <summary>Nada suara edge-tts, mis. <c>+0Hz</c>.</summary>
    public string Pitch { get; set; } = "+0Hz";

    /// <summary>Alamat bind Kestrel.</summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>Port HTTP service kiosk.</summary>
    public int Port { get; set; } = 9292;

    /// <summary>Token opsional. Bila diisi, endpoint aksi wajib menyertakan <c>?token=</c>.</summary>
    public string Token { get; set; } = "";

    /// <summary>Jumlah pengulangan default bila request tidak menyebutkan <c>repeat</c>.</summary>
    public int Repeats { get; set; } = 1;

    /// <summary>Batas atas nilai <c>repeat</c> yang diterima.</summary>
    public int MaxRepeats { get; set; } = 3;

    /// <summary>Perintah pemutar audio. Kosong = deteksi otomatis (mpv/ffplay/cvlc/play/aplay).</summary>
    public string Player { get; set; } = "";

    /// <summary>
    /// Perintah edge-tts. Kosong = deteksi otomatis (<c>edge-tts</c> atau
    /// <c>python3 -m edge_tts</c>).
    /// </summary>
    public string EdgeTtsCommand { get; set; } = "";

    /// <summary>Eja digit pada nomor antrian agar terdengar jelas (mis. <c>A003</c> → "A, nol, nol, tiga").</summary>
    public bool SpellNumbers { get; set; } = true;

    /// <summary>Template teks panggilan awal. Token: <c>{number}</c>, <c>{counter}</c>, <c>{service}</c>, <c>{customer}</c>.</summary>
    public string CallTemplate { get; set; } =
        "Nomor antrian {number}{service}{customer}, silakan menuju {counter}.";

    /// <summary>Template teks saat nomor sedang diproses (<c>phase=serving</c>). Token sama seperti <see cref="CallTemplate"/>.</summary>
    public string ServingTemplate { get; set; } =
        "Nomor antrian {number}{service}{customer}, sedang diproses di {counter}.";

    /// <summary>Template teks saat nomor selesai (<c>phase=done</c>). Token sama seperti <see cref="CallTemplate"/>.</summary>
    public string DoneTemplate { get; set; } =
        "Nomor antrian {number}{service}{customer}, telah selesai dilayani. Terima kasih.";

    /// <summary>Panjang maksimum teks pada <c>/speak</c>.</summary>
    public int MaxTextLength { get; set; } = 1500;

    /// <summary>Kapasitas antrian bicara. Bila penuh, request ditolak dengan 503.</summary>
    public int MaxQueue { get; set; } = 10;

    /// <summary>Terapkan override dari argumen command line di atas nilai appsettings.</summary>
    public static void ApplyCommandLine(IConfiguration config, KioskOptions options)
    {
        if (!string.IsNullOrWhiteSpace(config["engine"])) options.Engine = config["engine"]!;
        if (!string.IsNullOrWhiteSpace(config["voice"])) options.Voice = config["voice"]!;
        if (!string.IsNullOrWhiteSpace(config["rate"])) options.Rate = config["rate"]!;
        if (!string.IsNullOrWhiteSpace(config["volume"])) options.Volume = config["volume"]!;
        if (!string.IsNullOrWhiteSpace(config["pitch"])) options.Pitch = config["pitch"]!;
        if (!string.IsNullOrWhiteSpace(config["host"])) options.Host = config["host"]!;
        if (int.TryParse(config["port"], out var port)) options.Port = port;
        if (config["token"] is { } token) options.Token = token;
        if (int.TryParse(config["repeat"], out var repeat)) options.Repeats = repeat;
        if (int.TryParse(config["max-repeats"], out var maxRepeats)) options.MaxRepeats = maxRepeats;
        if (!string.IsNullOrWhiteSpace(config["player"])) options.Player = config["player"]!;
        if (!string.IsNullOrWhiteSpace(config["edge-tts"])) options.EdgeTtsCommand = config["edge-tts"]!;
        if (bool.TryParse(config["spell-numbers"], out var spell)) options.SpellNumbers = spell;
        if (!string.IsNullOrWhiteSpace(config["call-template"])) options.CallTemplate = config["call-template"]!;
        if (!string.IsNullOrWhiteSpace(config["serving-template"])) options.ServingTemplate = config["serving-template"]!;
        if (!string.IsNullOrWhiteSpace(config["done-template"])) options.DoneTemplate = config["done-template"]!;
        if (int.TryParse(config["max-text-length"], out var maxText)) options.MaxTextLength = maxText;
        if (int.TryParse(config["max-queue"], out var maxQueue)) options.MaxQueue = maxQueue;
    }

    /// <summary>Normalisasi + validasi nilai konfigurasi.</summary>
    public void Validate()
    {
        if (!string.Equals(Engine, "edge", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Engine '{Engine}' belum didukung. Jalankan dengan --engine edge.");
        }

        Engine = "edge";

        if (string.IsNullOrWhiteSpace(Voice))
            throw new InvalidOperationException("Voice tidak boleh kosong (mis. --voice id-ID-GadisNeural).");

        if (string.IsNullOrWhiteSpace(Host))
            Host = "0.0.0.0";

        if (Port is < 1 or > 65535)
            throw new InvalidOperationException($"Port tidak valid: {Port}.");

        if (Repeats < 1) Repeats = 1;
        if (MaxRepeats < 1) MaxRepeats = 1;
        if (MaxQueue < 1) MaxQueue = 1;
        if (MaxTextLength < 1) MaxTextLength = 1500;

        if (string.IsNullOrWhiteSpace(CallTemplate))
            CallTemplate = "Nomor antrian {number}{service}{customer}, silakan menuju {counter}.";

        if (string.IsNullOrWhiteSpace(ServingTemplate))
            ServingTemplate = "Nomor antrian {number}{service}{customer}, sedang diproses di {counter}.";

        if (string.IsNullOrWhiteSpace(DoneTemplate))
            DoneTemplate = "Nomor antrian {number}{service}{customer}, telah selesai dilayani. Terima kasih.";
    }
}
