using System.Text;
using System.Text.RegularExpressions;
using BackendTts.Models;

namespace BackendTts.Helpers;

/// <summary>
/// Penyusun teks yang dibacakan: eja digit agar nomor antrian terdengar jelas
/// dan isi template panggilan.
/// </summary>
public static partial class SpeechText
{
    private static readonly string[] DigitWords =
    {
        "nol", "satu", "dua", "tiga", "empat",
        "lima", "enam", "tujuh", "delapan", "sembilan",
    };

    /// <summary>
    /// Ubah kode menjadi kata per bagian. Contoh: <c>A003</c> → "A, nol, nol, tiga",
    /// <c>12</c> → "satu, dua" (bukan "dua belas", agar jelas di ruang tunggu).
    /// </summary>
    public static string Spell(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var tokens = new List<string>();
        var letters = new StringBuilder();

        void FlushLetters()
        {
            if (letters.Length == 0) return;
            tokens.Add(letters.ToString());
            letters.Clear();
        }

        foreach (var ch in value.Trim())
        {
            if (char.IsDigit(ch))
            {
                FlushLetters();
                tokens.Add(DigitWords[ch - '0']);
            }
            else if (char.IsLetter(ch))
            {
                letters.Append(ch);
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or '/' or '.')
            {
                FlushLetters();
            }
            else
            {
                FlushLetters();
                tokens.Add(ch.ToString());
            }
        }

        FlushLetters();
        return string.Join(", ", tokens.Where(t => t.Length > 0));
    }

    /// <summary>Bentuk teks panggilan dari template yang diberikan (default: <see cref="KioskOptions.CallTemplate"/>).</summary>
    public static string BuildCallText(
        KioskOptions options,
        string number,
        string? counter,
        string? service,
        string? template = null,
        string? customerName = null)
    {
        var numberText = options.SpellNumbers ? Spell(number) : number.Trim();
        // Nama loket boleh sudah mengandung kata "loket" (mis. "Loket 1");
        // buang awalan itu dulu agar tidak menjadi "loket loket …".
        var counterValue = counter?.Trim() ?? string.Empty;
        if (counterValue.StartsWith("loket", StringComparison.OrdinalIgnoreCase))
            counterValue = counterValue["loket".Length..].Trim();
        var counterText = string.IsNullOrWhiteSpace(counterValue)
            ? "loket pelayanan"
            : $"loket {(options.SpellNumbers ? Spell(counterValue) : counterValue)}";
        var serviceText = string.IsNullOrWhiteSpace(service)
            ? string.Empty
            : $", layanan {service.Trim()}";
        // Berupa frasa lengkap " atas nama {nama}" agar saat nama kosong
        // tidak ada sisa frasa menggantung (mis. "atas nama" tanpa nama).
        var customerText = string.IsNullOrWhiteSpace(customerName)
            ? string.Empty
            : $" atas nama {customerName!.Trim()}";

        var text = (string.IsNullOrWhiteSpace(template) ? options.CallTemplate : template!)
            .Replace("{number}", numberText)
            .Replace("{counter}", counterText)
            .Replace("{service}", serviceText)
            .Replace("{customer}", customerText);

        return Cleanup(text);
    }

    /// <summary>Rapikan spasi & koma berlebih yang muncul akibat token kosong.</summary>
    private static string Cleanup(string text)
    {
        text = MultiSpaceRegex().Replace(text, " ");
        text = CommaSpaceRegex().Replace(text, ", ");
        text = DoubleCommaRegex().Replace(text, ",");
        text = CommaBeforePunctRegex().Replace(text, "$1");
        return text.Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\s*,\s*")]
    private static partial Regex CommaSpaceRegex();

    [GeneratedRegex(@",\s*,")]
    private static partial Regex DoubleCommaRegex();

    [GeneratedRegex(@",(\s*[.!?])")]
    private static partial Regex CommaBeforePunctRegex();
}
