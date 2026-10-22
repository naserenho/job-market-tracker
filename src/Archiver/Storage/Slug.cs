using System.Text;

namespace Archiver.Storage;

public static class Slug
{
    // Lowercase letters/digits joined by single dashes; '#' and '+' are spelled out so "c#" and "c" stay distinct.
    public static string From(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (ch == '#') sb.Append("sharp");
            else if (ch == '+') sb.Append("plus");
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        return sb.ToString().Trim('-');
    }
}
