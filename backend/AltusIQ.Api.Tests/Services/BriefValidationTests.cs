using AltusIQ.Api.Investigation;
using AltusIQ.Api.Tests.Fakes;

namespace AltusIQ.Api.Tests.Services;

public class BriefValidationTests
{
    [Fact]
    public void A_fenced_reply_with_prose_around_it_still_parses()
    {
        var text = "Here you go:\n```json\n" + FakeLlmClient.ValidBrief + "\n```\nLet me know.";

        Assert.True(BriefValidator.TryParse(text, out var brief, out var error), error);
        Assert.Equal(1, brief!.Explanations.Count);
    }

    [Theory]
    [InlineData("""{"summary":"","observations":[],"explanations":[],"unknowns":[],"evidence_quality":"poor","recommended_follow_up":"x"}""", "summary")]
    [InlineData("""{"summary":"s","observations":"none","explanations":[],"unknowns":[],"evidence_quality":"poor","recommended_follow_up":"x"}""", "observations")]
    [InlineData("""{"summary":"s","observations":[],"explanations":[{"hypothesis":"h","evidence":[],"plausibility":"certain"}],"unknowns":[],"evidence_quality":"poor","recommended_follow_up":"x"}""", "plausibility")]
    [InlineData("""{"summary":"s","observations":[],"explanations":[],"unknowns":[],"evidence_quality":"poor"}""", "recommended_follow_up")]
    [InlineData("""{"summary":"s","observations":[],"explanations":[{"hypothesis":"h","evidence":[],"plausibility":"low","confidence":0.9}],"unknowns":[],"evidence_quality":"poor","recommended_follow_up":"x"}""", "confidence")]
    [InlineData("""{"summary":"s","cause":"weather","observations":[],"explanations":[],"unknowns":[],"evidence_quality":"poor","recommended_follow_up":"x"}""", "cause")]
    public void Shape_violations_are_rejected_with_the_offending_field_named(string json, string field)
    {
        Assert.False(BriefValidator.TryParse(json, out _, out var error));
        Assert.Contains(field, error);
    }

    [Fact]
    public void Speculation_is_a_high_plausibility_explanation_on_poor_evidence()
    {
        var poorHigh = new InvestigationBrief("s", [], [new Explanation("h", [], "high")], [], "poor", "f");
        var poorLow = new InvestigationBrief("s", [], [new Explanation("h", [], "low")], [], "poor", "f");
        var goodHigh = new InvestigationBrief("s", [], [new Explanation("h", [], "high")], [], "good", "f");

        Assert.True(poorHigh.IsSpeculative);
        Assert.False(poorLow.IsSpeculative);
        Assert.False(goodHigh.IsSpeculative);
    }

    [Fact]
    public void Groundedness_tokens_are_four_letter_codes_and_integers_of_100_or_more()
    {
        var tokens = GroundednessChecker.Tokens(
            "ESSA to EKCH at 11000.5 m, FL350, 10,000 ft, 59.6519 N, in 12 min, per ICAO rules").ToList();

        Assert.Contains("ESSA", tokens);
        Assert.Contains("EKCH", tokens);
        Assert.Contains("11000", tokens);
        // Decimal fractions are not figures the model could have invented on
        // their own; "59.6519" is a coordinate, not the number 6519.
        Assert.DoesNotContain("6519", tokens);
        Assert.DoesNotContain("ICAO", tokens);
        // A flight level is a figure: converting a tool's metres into FL350 is
        // a claim the evidence did not make, so it must appear there to pass.
        Assert.Contains("350", tokens);
        Assert.DoesNotContain("000", tokens);
        Assert.DoesNotContain("12", tokens);
    }

    [Fact]
    public void A_typographic_space_inside_a_figure_joins_its_digit_groups()
    {
        // Not a theory: xUnit 2.4 gives InlineData strings that differ only in
        // non-ASCII whitespace the same test id and silently runs one of them.
        string[] texts =
        [
            @"held 11 277 m", // escaped, as brief.ToJson() writes it
            "held 11 277 m",  // the narrow no-break space itself
            "held 11 277 m"        // a no-break space
        ];

        Assert.All(texts, text => Assert.Equal(["11277"], GroundednessChecker.Tokens(text).ToList()));
    }

    [Fact]
    public void A_brief_quoting_a_grouped_figure_from_the_evidence_is_grounded()
    {
        // gpt-oss-120b on the 7600 cruise case: the evidence said 11277 and the
        // brief said "11 277 m" with U+202F, which used to fail as 202 and 277.
        Assert.True(BriefValidator.TryParse(
            """{"summary":"Held 11 277 m at cruise.","observations":[],"explanations":[],"unknowns":[],"evidence_quality":"good","recommended_follow_up":"x"}""",
            out var brief, out var error), error);

        var result = GroundednessChecker.Check(brief!.ToJson(), ["""{"first_altitude":11277}"""]);

        Assert.True(result.Grounded, string.Join(", ", result.Unsupported));
    }

    [Fact]
    public void A_comma_grouped_figure_is_one_number_in_the_brief_but_commas_in_evidence_still_separate()
    {
        // The next live run wrote "11,277 m" with a comma and was flagged on 277.
        // The trail row's "40,240" must stay two values, or 240 m/s would fail.
        var evidence = """{"first_altitude":11277,"trail":[[1754046600,13.56,58.17,11277,40,240]]}""";

        var result = GroundednessChecker.Check(
            """{"summary":"Held 11,277 m at 240 m/s, about 11,300 m."}""", [evidence]);

        Assert.Equal(["11300"], result.Unsupported);
    }

    [Fact]
    public void Joining_digit_groups_neither_hides_a_rounded_figure_nor_merges_two_figures()
    {
        var rounded = GroundednessChecker.Check(@"{""summary"":""about 11 300 m""}", ["""{"first_altitude":11277}"""]);
        Assert.Equal(["11300"], rounded.Unsupported);

        Assert.Equal(["350", "300"], GroundednessChecker.Tokens("FL350 300 km east").ToList());
    }

    [Fact]
    public void Airport_bearing_runs_from_the_position_to_the_airport()
    {
        // The tool description and the prompt both promise this direction; a
        // brief that read it backwards put an aircraft on the wrong side of ESGG.
        var east = new AirportIndex.Airport("EAST", null, null, 0, 1);
        var south = new AirportIndex.Airport("SOUT", null, null, -1, 0);
        var index = new AirportIndex([east, south]);

        var nearest = index.Nearest(0, 0, 2).ToDictionary(n => n.Airport.Icao, n => n.BearingDeg);

        Assert.Equal(90, nearest["EAST"], 0.5);
        Assert.Equal(180, nearest["SOUT"], 0.5);
    }

    [Fact]
    public void Budget_resets_on_the_UTC_day_boundary_not_after_24_hours()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 8, 1, 23, 59, 0, TimeSpan.Zero));
        var budget = new LlmCallBudget(
            Microsoft.Extensions.Options.Options.Create(new Models.LlmSettings { MaxCallsPerDay = 1, MinSecondsBetweenCalls = 3 }),
            clock);

        Assert.True(budget.TryReserve());
        Assert.False(budget.TryReserve());
        Assert.Equal(TimeSpan.FromSeconds(3), budget.TimeUntilNextAllowed());

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(budget.TryReserve());
        Assert.Equal(1, budget.CallsToday);
    }

    [Fact]
    public void Budget_spacing_is_measured_from_the_previous_call_not_the_current_one()
    {
        var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        var budget = new LlmCallBudget(
            Microsoft.Extensions.Options.Options.Create(new Models.LlmSettings { MaxCallsPerDay = 10, MinSecondsBetweenCalls = 3 }),
            clock);

        // No previous call, so no wait.
        Assert.True(budget.TryReserve(out var first));
        Assert.Equal(TimeSpan.Zero, first);

        // Immediately after the first call: the full spacing.
        Assert.True(budget.TryReserve(out var second));
        Assert.Equal(TimeSpan.FromSeconds(3), second);

        // Spaced from the second call's scheduled time, which is 3 s from now.
        Assert.True(budget.TryReserve(out var third));
        Assert.Equal(TimeSpan.FromSeconds(6), third);

        // The spacing has long passed: no wait.
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.True(budget.TryReserve(out var later));
        Assert.Equal(TimeSpan.Zero, later);
    }
}
