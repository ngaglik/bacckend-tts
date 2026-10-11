using System.Threading.Channels;
using BackendTts.Helpers;
using BackendTts.Models;

namespace BackendTts.Services;

/// <summary>
/// Antrian bicara + worker latar belakang. Permintaan HTTP langsung
/// dikembalikan (fire-and-forget) sehingga pemanggil seperti
/// <c>erp/backend</c> tidak menunggu proses bicara selesai.
/// </summary>
public sealed class SpeechService : BackgroundService
{
    private readonly KioskOptions _options;
    private readonly ITtsEngine _engine;
    private readonly AudioPlayer _player;
    private readonly ILogger<SpeechService> _logger;
    private readonly Channel<SpeechJob> _channel;
    private readonly string _workDir;
    private readonly object _gate = new();

    private CancellationTokenSource? _currentCts;
    private string? _currentText;

    public SpeechService(
        KioskOptions options,
        ITtsEngine engine,
        AudioPlayer player,
        ILogger<SpeechService> logger)
    {
        _options = options;
        _engine = engine;
        _player = player;
        _logger = logger;
        _workDir = Path.Combine(Path.GetTempPath(), "backend-tts");
        Directory.CreateDirectory(_workDir);

        _channel = Channel.CreateBounded<SpeechJob>(new BoundedChannelOptions(Math.Max(1, options.MaxQueue))
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public bool IsSpeaking
    {
        get { lock (_gate) return _currentText is not null; }
    }

    public string? CurrentText
    {
        get { lock (_gate) return _currentText; }
    }

    public int QueuedCount => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

    /// <summary>
    /// Nama pemutar audio yang dipakai — untuk diagnostik kualitas suara
    /// (mis. mpv vs cvlc vs aplay). Kosong bila belum terdeteksi.
    /// </summary>
    public string PlayerName
    {
        get
        {
            try { return _player.PlayerName; }
            catch { return string.Empty; }
        }
    }

    public EnqueueResult Enqueue(SpeechSource source, string text, int repeat)
    {
        var job = new SpeechJob(source, text, repeat, DateTime.Now);

        if (!_channel.Writer.TryWrite(job))
            return new EnqueueResult(false, $"Antrian TTS penuh ({_options.MaxQueue} permintaan).", QueuedCount);

        return new EnqueueResult(true, "Teks dikirim ke pengeras suara.", QueuedCount);
    }

    public StopResult Stop()
    {
        var drained = 0;
        while (_channel.Reader.TryRead(out _)) drained++;

        bool wasSpeaking;
        lock (_gate)
        {
            wasSpeaking = _currentText is not null;
            _currentCts?.Cancel();
        }

        var message = wasSpeaking ? "Bacaan dihentikan." : "Tidak ada bacaan yang sedang berjalan.";
        return new StopResult(true, message, drained);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Worker TTS aktif (engine={Engine}, voice={Voice}, player={Player})",
            _engine.Name,
            _engine.Voice,
            ResolvePlayerName());

        try
        {
            await foreach (var job in _channel.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
                await ProcessAsync(job, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Service dihentikan.
        }
    }

    private async Task ProcessAsync(SpeechJob job, CancellationToken stoppingToken)
    {
        var mediaFile = Path.Combine(_workDir, $"speech-{Guid.NewGuid():N}.mp3");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        lock (_gate)
        {
            _currentCts = cts;
            _currentText = job.Text;
        }

        try
        {
            await _engine.SynthesizeAsync(job.Text, mediaFile, cts.Token).ConfigureAwait(false);

            for (var i = 0; i < job.Repeat; i++)
            {
                cts.Token.ThrowIfCancellationRequested();
                await _player.PlayAsync(mediaFile, cts.Token).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "[{Source}] Selesai dibacakan ({Repeat}x): {Text}",
                job.Source,
                job.Repeat,
                Truncate(job.Text));
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[{Source}] Bacaan dibatalkan: {Text}", job.Source, Truncate(job.Text));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Source}] Gagal membacakan: {Text}", job.Source, Truncate(job.Text));
        }
        finally
        {
            lock (_gate)
            {
                _currentCts = null;
                _currentText = null;
            }

            FileUtil.TryDelete(mediaFile);
        }
    }

    private string ResolvePlayerName()
    {
        try
        {
            return _player.PlayerName;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pemutar audio belum siap saat startup.");
            return "(belum terdeteksi)";
        }
    }

    private static string Truncate(string text, int maxLength = 120) =>
        text.Length <= maxLength ? text : text[..maxLength] + "…";
}
