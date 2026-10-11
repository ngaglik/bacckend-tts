using BackendTts.Helpers;
using BackendTts.Models;

namespace BackendTts.Services;

/// <summary>Pemutar audio lokal (subprocess).</summary>
public sealed class AudioPlayer
{
    private static readonly (string Executable, string[] Arguments, bool NeedsWav)[] Candidates =
    {
        ("mpv", new[] { "--no-video", "--really-quiet", "--no-terminal" }, false),
        ("ffplay", new[] { "-nodisp", "-autoexit", "-loglevel", "quiet", "-hide_banner" }, false),
        ("cvlc", new[] { "--play-and-exit", "--quiet", "--intf", "dummy" }, false),
        ("play", new[] { "-q" }, false),
        ("paplay", Array.Empty<string>(), true),
        ("aplay", new[] { "-q" }, true),
    };

    private static readonly HashSet<string> WavOnlyPlayers =
        new(StringComparer.OrdinalIgnoreCase) { "aplay", "paplay" };

    private static readonly TimeSpan PlaybackTimeout = TimeSpan.FromMinutes(10);

    private readonly KioskOptions _options;
    private readonly ILogger<AudioPlayer> _logger;
    private readonly string _workDir;
    private readonly object _gate = new();

    private PlayerSpec? _spec;

    public AudioPlayer(KioskOptions options, ILogger<AudioPlayer> logger)
    {
        _options = options;
        _logger = logger;
        _workDir = Path.Combine(Path.GetTempPath(), "backend-tts");
        Directory.CreateDirectory(_workDir);
    }

    /// <summary>Nama pemutar yang dipakai (untuk ditampilkan di /health).</summary>
    public string PlayerName => Resolve().Executable;

    public async Task PlayAsync(string mediaFile, CancellationToken cancellationToken)
    {
        var spec = Resolve();
        var fileToPlay = mediaFile;
        string? converted = null;

        try
        {
            if (spec.NeedsWav && !mediaFile.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            {
                converted = await MediaConverter
                    .TryConvertToWavAsync(mediaFile, _workDir, _logger, cancellationToken)
                    .ConfigureAwait(false);

                if (converted is not null) fileToPlay = converted;
                else _logger.LogWarning(
                    "Tidak dapat mengubah {File} ke WAV; mencoba memutar langsung memakai {Player}.",
                    Path.GetFileName(mediaFile),
                    spec.Executable);
            }

            var arguments = spec.Arguments.Concat(new[] { fileToPlay }).ToArray();
            var result = await ProcessRunner
                .RunAsync(spec.Executable, arguments, PlaybackTimeout, cancellationToken)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Pemutar '{spec.Executable}' keluar dengan kode {result.ExitCode}. " +
                    ProcessRunner.FirstLines(result.StdErr));
            }
        }
        finally
        {
            FileUtil.TryDelete(converted);
        }
    }

    private PlayerSpec Resolve()
    {
        lock (_gate)
        {
            if (_spec is not null) return _spec;

            if (!string.IsNullOrWhiteSpace(_options.Player))
            {
                var parts = ArgumentSplitter.Split(_options.Player);
                if (parts.Count == 0)
                    throw new InvalidOperationException("Konfigurasi Player kosong / tidak valid.");

                var executable = parts[0];
                var needsWav = WavOnlyPlayers.Contains(Path.GetFileNameWithoutExtension(executable));
                _spec = new PlayerSpec(executable, parts.Skip(1).ToArray(), needsWav);

                if (!executable.Contains(Path.DirectorySeparatorChar) && ExecutableLocator.Which(executable) is null)
                {
                    _logger.LogWarning(
                        "Pemutar '{Player}' tidak ditemukan di PATH.", executable);
                }
                else
                {
                    _logger.LogInformation(
                        "Pemutar audio (konfigurasi): {Player} {Args}",
                        executable,
                        string.Join(' ', _spec.Arguments));
                }

                return _spec;
            }

            foreach (var (executable, arguments, needsWav) in Candidates)
            {
                if (ExecutableLocator.Which(executable) is { } fullPath)
                {
                    _spec = new PlayerSpec(fullPath, arguments, needsWav);
                    _logger.LogInformation("Pemutar audio terdeteksi: {Player}", executable);
                    return _spec;
                }
            }

            throw new InvalidOperationException(
                "Tidak menemukan pemutar audio (mpv/ffplay/cvlc/play/aplay). " +
                "Pasang salah satunya atau set manual: --player \"mpv --no-video --really-quiet\".");
        }
    }

    private sealed record PlayerSpec(string Executable, string[] Arguments, bool NeedsWav);
}
