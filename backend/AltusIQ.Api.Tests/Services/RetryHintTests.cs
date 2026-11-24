using System.Net.Http.Headers;
using AltusIQ.Api.Llm;

namespace AltusIQ.Api.Tests.Services;

public class RetryHintTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 24, 17, 48, 0, TimeSpan.Zero);

    // The body Groq returned on the live 7600 case, organisation id removed.
    private const string GroqTpmBody =
        """{"error":{"message":"Rate limit reached for model `openai/gpt-oss-120b` in organization `org_x` service tier `on_demand` on tokens per minute (TPM): Limit 8000, Used 5862, Requested 3368. Please try again in 9.225s. Need more tokens?","type":"tokens","code":"rate_limit_exceeded"}}""";

    [Fact]
    public void Groqs_try_again_in_text_is_read_when_there_is_no_header()
    {
        Assert.Equal(TimeSpan.FromSeconds(9.225), OpenAiCompatibleChatClient.RetryHint(null, GroqTpmBody, Now));
    }

    [Theory]
    [InlineData("Please try again in 2m59.5s.", 179.5)]
    [InlineData("Please try again in 1h2m3s.", 3723)]
    [InlineData("Please try again in 530ms.", 0.53)]
    public void Go_style_durations_add_up_across_their_units(string body, double seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), OpenAiCompatibleChatClient.RetryHint(null, body, Now));
    }

    [Fact]
    public void The_longer_of_header_and_body_wins_so_a_rounded_header_never_retries_early()
    {
        var rounded = new RetryConditionHeaderValue(TimeSpan.FromSeconds(9));
        var longer = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));

        Assert.Equal(TimeSpan.FromSeconds(9.225), OpenAiCompatibleChatClient.RetryHint(rounded, GroqTpmBody, Now));
        Assert.Equal(TimeSpan.FromSeconds(12), OpenAiCompatibleChatClient.RetryHint(longer, GroqTpmBody, Now));
    }

    [Fact]
    public void A_header_date_counts_from_now_and_a_past_date_or_no_hint_is_null()
    {
        var future = new RetryConditionHeaderValue(Now.AddSeconds(20));
        var past = new RetryConditionHeaderValue(Now.AddSeconds(-5));

        Assert.Equal(TimeSpan.FromSeconds(20), OpenAiCompatibleChatClient.RetryHint(future, "{}", Now));
        Assert.Null(OpenAiCompatibleChatClient.RetryHint(past, "{}", Now));
        Assert.Null(OpenAiCompatibleChatClient.RetryHint(null, """{"error":{"message":"Too many requests"}}""", Now));
    }
}
