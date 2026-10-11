using System.Text;

namespace BackendTts.Helpers;

/// <summary>Pemecah string perintah menjadi executable + daftar argumen (menghormati tanda kutip).</summary>
public static class ArgumentSplitter
{
    public static IReadOnlyList<string> Split(string command)
    {
        var parts = new List<string>();
        if (string.IsNullOrWhiteSpace(command)) return parts;

        var buffer = new StringBuilder();
        var quote = '\0';

        foreach (var ch in command)
        {
            if (quote != '\0')
            {
                if (ch == quote) quote = '\0';
                else buffer.Append(ch);
            }
            else if (ch is '"' or '\'')
            {
                quote = ch;
            }
            else if (char.IsWhiteSpace(ch))
            {
                if (buffer.Length > 0)
                {
                    parts.Add(buffer.ToString());
                    buffer.Clear();
                }
            }
            else
            {
                buffer.Append(ch);
            }
        }

        if (buffer.Length > 0) parts.Add(buffer.ToString());
        return parts;
    }
}
