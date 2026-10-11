using BackendTts.Helpers;
using BackendTts.Models;

namespace BackendTts.Services;

/// <summary>
/// Engine TTS <c>edge</c> — memanggil CLI <c>edge-tts</c> (Microsoft Edge Read
/// Aloud) dengan voice <c>id-ID-GadisNeural</c>. Menghasilkan berkas MP3.
///
/// Perintah dipilih otomatis: <c>edge-tts</c> bila ada di PATH, jika tidak
/// <c>python3 -m edge_tts</c>. Bisa di-override dengan <c>--edge-tts</c>
/// atau <c>Kiosk:EdgeTtsCommand</c>.
/// </summary>
public sealed class EdgeTtsEngine : ITtsEngine
{
    private static readonly TimeSpan SynthesisTimeout = TimeSpan.FromSeconds(60);

    private readonly KioskOptions _options;
    private readonly ILogger<EdgeTtsEngine> _logger;
    private readonly string _workDir;
    private readonly object _gate = new();

    private IReadOnlyList<string>? _command;

    public EdgeTtsEngine(KioskOptions options, ILogger<EdgeTtsEngine> logger)
    {
        _options = options;
        _logger = logger;
        _workDir = Path.Combine(Path.GetTempPath(), "backend-tts");
        Directory.CreateDirectory(_workDir);
    }

    public string Name => "edge";

    public string Voice => _options.Voice;

    public async Task SynthesizeAsync(string text, string outputFile, CancellationToken cancellationToken)
    {
        var command = ResolveCommand();
        var textFile = Path.Combine(_workDir, $"text-{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllTextAsync(textFile, text, new System.Text.UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);

            var arguments = new List<string>(command.Skip(1))
            {
                "--voice", _options.Voice,
                "--rate", _options.Rate,
                "--volume", _options.Volume,
                "--pitch", _options.Pitch,
                "--file", textFile,
                "--write-media", outputFile,
            };

            var result = await ProcessRunner
                .RunAsync(command[0], arguments, SynthesisTimeout, cancellationToken)
                .ConfigureAwait(false);

            if (result.TimedOut)
                throw new TtsException($"edge-tts melebihi batas waktu {SynthesisTimeout.TotalSeconds:0} detik.");

            if (!File.Exists(outputFile) || new FileInfo(outputFile).Length == 0)
            {
                var detail = ProcessRunner.FirstLines(result.StdErr);
                throw new TtsException(
                    $"edge-tts tidak menghasilkan audio (kode {result.ExitCode}). {detail}".Trim());
            }

            if (result.ExitCode != 0)
            {
                _logger.LogWarning(
                    "edge-tts keluar dengan kode {Code} tetapi audio terbentuk: {Error}",
                    result.ExitCode,
                    ProcessRunner.FirstLines(result.StdErr));
            }
        }
        catch (OperationCanceledException)
        {
            FileUtil.TryDelete(outputFile);
            throw;
        }
        finally
        {
            FileUtil.TryDelete(textFile);
        }
    }

    public async Task<IReadOnlyList<string>> ListVoicesAsync(CancellationToken cancellationToken)
    {
        var command = ResolveCommand();
        var arguments = command.Skip(1).Concat(new[] { "--list-voices" }).ToArray();

        var result = await ProcessRunner
            .RunAsync(command[0], arguments, TimeSpan.FromSeconds(30), cancellationToken)
            .ConfigureAwait(false);

        if (result.ExitCode != 0)
            throw new TtsException($"Gagal mengambil daftar voice. {ProcessRunner.FirstLines(result.StdErr)}");

        var voices = result.StdOut
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("ShortName:", StringComparison.OrdinalIgnoreCase))
            .Select(line => line["ShortName:".Length..].Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return voices;
    }

    private IReadOnlyList<string> ResolveCommand()
    {
        lock (_gate)
        {
            if (_command is not null) return _command;

            if (!string.IsNullOrWhiteSpace(_options.EdgeTtsCommand))
            {
                var configured = ArgumentSplitter.Split(_options.EdgeTtsCommand);
                if (configured.Count == 0)
                    throw new TtsException("Konfigurasi EdgeTtsCommand kosong / tidak valid.");

                _command = configured;
                _logger.LogInformation("edge-tts dari konfigurasi: {Command}", string.Join(' ', configured));
                return _command;
            }

            // 'edge-tts' dari PATH — tapi pastikan bisa dijalankan, karena venv
            // yang dipindah/dikopi bisa menyisakan skrip yang gagal start.
            var edgeTts = ExecutableLocator.Which("edge-tts");
            if (edgeTts is not null)
            {
                if (CanExecute(edgeTts))
                {
                    _command = new[] { edgeTts };
                    _logger.LogInformation("engine edge memakai executable '{Path}'.", edgeTts);
                    return _command;
                }

                _logger.LogWarning(
                    "'{Path}' ditemukan tetapi tidak dapat dijalankan (venv rusak?). " +
                    "Mencoba alternatif lain.",
                    edgeTts);
            }

            var python = ExecutableLocator.Which("python3") ?? ExecutableLocator.Which("python");
            if (python is not null)
            {
                try
                {
                    var probe = ProcessRunner
                        .RunAsync(python, new[] { "-c", "import edge_tts" }, TimeSpan.FromSeconds(15))
                        .GetAwaiter()
                        .GetResult();

                    if (probe.ExitCode == 0)
                    {
                        _command = new[] { python, "-m", "edge_tts" };
                        _logger.LogInformation("engine edge memakai '{Python} -m edge_tts'.", python);
                        return _command;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Gagal memeriksa modul edge_tts pada python.");
                }
            }

            throw new TtsException(
                "edge-tts tidak ditemukan atau tidak dapat dijalankan. " +
                "Jalankan './setup.sh' untuk membuat venv lokal (jangan salin folder .venv " +
                "antar mesin), atau pasang dengan 'pip install edge-tts', " +
                "atau set perintahnya: --edge-tts \"python3 -m edge_tts\".");
        }
    }

    /// <summary>
    /// Cek executable benar-benar dapat dijalankan, bukan sekadar ada di disk.
    /// Venv yang dipindah/dikopi menyisakan skrip dengan shebang menuju
    /// interpreter lama sehingga <see cref="System.Diagnostics.Process.Start"/> gagal.
    /// </summary>
    private bool CanExecute(string executable)
    {
        try
        {
            var result = ProcessRunner
                .RunAsync(executable, new[] { "--help" }, TimeSpan.FromSeconds(15))
                .GetAwaiter()
                .GetResult();
            if (result.TimedOut) return false;

            // Exit 0 = skrip benar-benar jalan. Skrip rusak ber-shebang
            // interpreter hilang bisa lolos dengan kode != 0 (mis. 127).
            if (result.ExitCode != 0)
                _logger.LogDebug(
                    "Uji '{Path}' keluar dengan kode {Code}: {Error}",
                    executable,
                    result.ExitCode,
                    ProcessRunner.FirstLines(result.StdErr));

            return result.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Gagal menguji executable '{Path}'.", executable);
            return false;
        }
    }
}
