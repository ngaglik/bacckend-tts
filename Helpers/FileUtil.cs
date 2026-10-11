namespace BackendTts.Helpers;

/// <summary>Utilitas file kecil.</summary>
public static class FileUtil
{
    public static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Diabaikan: file sementara akan dibersihkan OS / siklus berikutnya.
        }
    }
}
