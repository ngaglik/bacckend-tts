using System.ComponentModel;
using System.Diagnostics;

namespace BackendTts.Helpers;

/// <summary>Hasil eksekusi proses eksternal.</summary>
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, bool TimedOut);

/// <summary>
/// Menjalankan proses eksternal (edge-tts, pemutar audio, konverter) dengan
/// penangkapan stdout/stderr dan dukungan pembatalan (proses dibunuh).
/// </summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        string? workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            startInfo.WorkingDirectory = workingDirectory;

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Mis. executable tidak ada, atau skrip dengan shebang menuju
            // interpreter yang sudah tidak ada (venv yang dipindah/dikopi).
            throw new InvalidOperationException(
                $"Gagal menjalankan '{executable}': {ex.Message}", ex);
        }

        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();

        var timedOut = false;
        var cancelled = false;

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            cancelled = cancellationToken.IsCancellationRequested;
            Kill(process);
        }

        var stdOut = await SafeReadAsync(stdOutTask).ConfigureAwait(false);
        var stdErr = await SafeReadAsync(stdErrTask).ConfigureAwait(false);

        if (cancelled)
            throw new OperationCanceledException("Proses dibatalkan.", cancellationToken);

        var exitCode = -1;
        try
        {
            if (process.HasExited) exitCode = process.ExitCode;
        }
        catch
        {
            // Proses sudah dibunuh / tidak tersedia.
        }

        return new ProcessResult(exitCode, stdOut, stdErr, timedOut);
    }

    /// <summary>Ambil beberapa baris pertama pesan error agar log tetap ringkas.</summary>
    public static string FirstLines(string? text, int maxLength = 300)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var collapsed = string.Join(' ', text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return collapsed.Length <= maxLength ? collapsed : collapsed[..maxLength] + "…";
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Diabaikan: proses mungkin sudah berhenti.
        }
    }

    private static async Task<string> SafeReadAsync(Task<string> reader)
    {
        try
        {
            return await reader.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }
}
