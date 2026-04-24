using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PartyPix.Web.Services;

public static partial class SlugService
{
    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonSlugChars();

    public static string Slugify(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return RandomSuffix(6);
        var lower = input.Trim().ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower.Normalize(NormalizationForm.FormD))
        {
            var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }
        var ascii = NonSlugChars().Replace(sb.ToString(), "-").Trim('-');
        if (ascii.Length > 40) ascii = ascii[..40].TrimEnd('-');
        return string.IsNullOrEmpty(ascii) ? RandomSuffix(6) : ascii;
    }

    public static string RandomSuffix(int length = 6)
    {
        const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
            sb.Append(alphabet[bytes[i] % alphabet.Length]);
        return sb.ToString();
    }

    /// <summary>Cookie token for guest sessions - 256 bits of entropy.</summary>
    public static string NewGuestToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
