namespace BackendTts.Models;

/// <summary>Asal permintaan bicara.</summary>
public enum SpeechSource
{
    /// <summary>Pengumuman nomor antrian (<c>GET /call</c>).</summary>
    Call,

    /// <summary>Teks bebas / halaman uji suara (<c>GET /speak</c>).</summary>
    Speak,
}

/// <summary>Satu permintaan yang menunggu dibacakan.</summary>
public sealed record SpeechJob(SpeechSource Source, string Text, int Repeat, DateTime CreatedAt);

/// <summary>Hasil masuk antrian.</summary>
public sealed record EnqueueResult(bool Ok, string Message, int Queued);

/// <summary>Hasil permintaan hentikan bacaan.</summary>
public sealed record StopResult(bool Ok, string Message, int Drained);
