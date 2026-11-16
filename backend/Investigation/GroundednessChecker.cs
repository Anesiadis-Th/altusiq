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

        var unsupported = Tokens(briefText)
            .Where(t => !corpus.Contains(t))
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        return new Result(unsupported.Count == 0, unsupported);
    }

    internal static IEnumerable<string> Tokens(string text)
    {
        foreach (Match m in IcaoCode().Matches(text))
        {
            if (!Allowlist.Contains(m.Value))
                yield return m.Value;
        }

        foreach (Match m in Integer().Matches(text))
        {
            // "000" from "10,000" parses to 0 and is never claimed as a figure.
            if (long.TryParse(m.Value, out var n) && n >= 100)
                yield return n.ToString();
        }
    }

    [GeneratedRegex(@"\b[A-Z]{4}\b")]
    private static partial Regex IcaoCode();

    [GeneratedRegex(@"(?<![\d.])\d{3,}(?![\d])")]
    private static partial Regex Integer();
}
