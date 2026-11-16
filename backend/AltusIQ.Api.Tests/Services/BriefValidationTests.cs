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
}
