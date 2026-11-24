using System.Text.RegularExpressions;

namespace AltusIQ.Api.Investigation;

/// <summary>
/// A cheap, mechanical check: every four-letter ICAO-looking code and every
/// integer of 100 or more in the brief must appear somewhere in what the model
/// was shown (tool outputs plus the event record). It cannot tell a true
/// sentence from a false one; it catches the model importing an airport or a
/// number from nowhere. Stored on the event either way.
/// </summary>
public static partial class GroundednessChecker
{
    public sealed record Result(bool Grounded, IReadOnlyList<string> Unsupported);

    // Four-letter aviation abbreviations that are not airport codes. The
    // checker would otherwise flag "ICAO" in a sentence about ICAO codes.
    private static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        "ICAO", "IATA", "ADSB", "GNSS", "ATIS", "RVSM", "TCAS", "ACAS", "JSON", "METAR"
    };

    public static Result Check(string briefText, IEnumerable<string?> evidenceTexts)
    {
        var corpus = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in evidenceTexts)
        {
            if (text is null) continue;
            foreach (var token in Tokens(text))
                corpus.Add(token);
        }

        var unsupported = Tokens(briefText, brief: true)
            .Where(t => !corpus.Contains(t))
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        return new Result(unsupported.Count == 0, unsupported);
    }

    internal static IEnumerable<string> Tokens(string text, bool brief = false)
    {
        text = Normalize(text);

        // Only the brief is prose, where "11,277 m" is one figure. In evidence a
        // comma separates JSON values, and joining "40,240" in a trail row would
        // hide the 240 it contains.
        if (brief)
            text = CommaGroupSeparator().Replace(text, "");

        foreach (Match m in IcaoCode().Matches(text))
        {
            if (!Allowlist.Contains(m.Value))
                yield return m.Value;
        }

        foreach (Match m in Integer().Matches(text))
        {
            // Outside the brief, "000" from "10,000" parses to 0 and is never
            // claimed as a figure.
            if (long.TryParse(m.Value, out var n) && n >= 100)
                yield return n.ToString();
        }
    }

    /// <summary>
    /// The brief arrives as JSON, and System.Text.Json escapes every space
    /// separator except U+0020, even under relaxed escaping. gpt-oss writes
    /// "11 277 m" with a narrow no-break space, so the text here read
    /// "11 277 m": the figures 202 and 277, neither of which the model
    /// wrote. Escapes are decoded first, then digit groups are joined across the
    /// typographic spaces that only separate thousands. A plain space is left
    /// alone, because "FL350 300 km" is two figures.
    /// </summary>
    private static string Normalize(string text)
    {
        var decoded = UnicodeEscape().Replace(text, m => ((char)Convert.ToUInt16(m.Groups[1].Value, 16)).ToString());
        return GroupSeparator().Replace(decoded, "");
    }

    [GeneratedRegex(@"\\u([0-9A-Fa-f]{4})")]
    private static partial Regex UnicodeEscape();

    // No-break space, thin space or narrow no-break space between a digit and a
    // group of exactly three digits.
    [GeneratedRegex(@"(?<=\d)[   ](?=\d{3}(?!\d))")]
    private static partial Regex GroupSeparator();

    [GeneratedRegex(@"(?<=\d),(?=\d{3}(?!\d))")]
    private static partial Regex CommaGroupSeparator();

    [GeneratedRegex(@"\b[A-Z]{4}\b")]
    private static partial Regex IcaoCode();

    [GeneratedRegex(@"(?<![\d.])\d{3,}(?![\d])")]
    private static partial Regex Integer();
}
