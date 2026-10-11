using BackendTts.Helpers;

namespace BackendTts.Services;

/// <summary>
/// Mengubah hasil edge-tts (MP3) menjadi WAV untuk pemutar yang tidak
/// mendukung MP3 (aplay/paplay). Memakai <c>ffmpeg</c> bila ada, jika tidak
/// <c>sox</c>.
/// </summary>
public static class MediaConverter
{
    private static readonly (string Executable, string[] Prefix)[] Converters =
    {
        ("ffmpeg", new[] { "-y", "-hide_banner", "-loglevel", "error" }),
        ("sox", Array.Empty<string>()),
    };

    public static async Task<string?> TryConvertToWavAsync(
        string input,
        string workDir,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var output = Path.Combine(workDir, Path.GetFileNameWithoutExtension(input) + ".wav");

        foreach (var (executable, prefix) in Converters)
        {
            var fullPath = ExecutableLocator.Which(executable);
            if (fullPath is null) continue;

            try
            {
                var arguments = prefix.Concat(new[] { input, output }).ToArray();
                var result = await ProcessRunner
                    .RunAsync(fullPath, arguments, TimeSpan.FromSeconds(60), cancellationToken)
                    .ConfigureAwait(false);

                if (result.ExitCode == 0 && File.Exists(output) && new FileInfo(output).Length > 0)
                {
                    logger.LogInformation("Audio dikonversi ke WAV memakai {Converter}.", executable);
                    return output;
                }

                logger.LogDebug("{Converter} gagal konversi: {Error}", executable, ProcessRunner.FirstLines(result.StdErr));
                FileUtil.TryDelete(output);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Konverter {Converter} gagal dijalankan.", executable);
            }
        }

        return null;
    }
}
