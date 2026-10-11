namespace BackendTts.Services;

/// <summary>Gagal melakukan sintesis suara (engine tidak tersedia / error).</summary>
public sealed class TtsException : Exception
{
    public TtsException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Mesin text-to-speech.</summary>
public interface ITtsEngine
{
    /// <summary>Nama engine, mis. <c>edge</c>.</summary>
    string Name { get; }

    /// <summary>Voice yang dipakai, mis. <c>id-ID-GadisNeural</c>.</summary>
    string Voice { get; }

    /// <summary>Sintesis <paramref name="text"/> ke berkas audio (mp3) di <paramref name="outputFile"/>.</summary>
    Task SynthesizeAsync(string text, string outputFile, CancellationToken cancellationToken);

    /// <summary>Daftar nama voice yang tersedia di engine.</summary>
    Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken cancellationToken);
}
