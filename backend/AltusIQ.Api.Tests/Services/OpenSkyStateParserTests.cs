using AltusIQ.Api.Services;

namespace AltusIQ.Api.Tests.Services;

public class OpenSkyStateParserTests
{
    // OpenSky state vector layout:
    // 0 icao24, 1 callsign, 2 origin_country, 3 time_position, 4 last_contact,
    // 5 longitude, 6 latitude, 7 baro_altitude, 8 on_ground, 9 velocity,
    // 10 true_track, 11 vertical_rate
    private const string ValidRow =
        """["4b1806","SWR123  ","Switzerland",1754000000,1754000000,8.5,47.4,11000.5,false,240.3,180.1,0.0]""";

    private static string StatesJson(params string[] rows) =>
        $$"""{"time":1754000000,"states":[{{string.Join(",", rows)}}]}""";

    [Fact]
    public void Parses_every_field_of_a_well_formed_row()
    {
        var result = OpenSkyStateParser.Parse(StatesJson(ValidRow));

        var aircraft = Assert.Single(result.Aircraft);
        Assert.Equal("4b1806", aircraft.Icao24);
        Assert.Equal("SWR123", aircraft.Callsign);
        Assert.Equal("Switzerland", aircraft.OriginCountry);
        Assert.Equal(8.5, aircraft.Longitude);
        Assert.Equal(47.4, aircraft.Latitude);
        Assert.Equal(11000.5, aircraft.BarometricAltitude);
        Assert.False(aircraft.OnGround);
        Assert.Equal(240.3, aircraft.Velocity);
        Assert.Equal(180.1, aircraft.Heading);
        Assert.Equal(0.0, aircraft.VerticalRate);
        Assert.Equal(1754000000, aircraft.LastContact);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void One_unusable_row_does_not_discard_the_rest_of_the_poll()
    {
        // The bug this guards: a single short row threw out of ParseStates and
        // binned the entire ~10k-aircraft poll.
        var result = OpenSkyStateParser.Parse(StatesJson(
            ValidRow,
            """["short"]""",
            ValidRow,
            """{"icao24":"4b1806"}""",
            ValidRow));

        Assert.Equal(3, result.Aircraft.Count);
        Assert.Equal(2, result.Skipped);
    }

    [Theory]
    // shorter than the 12 fields the parser reads
    [InlineData("""["4b1806","SWR123","Switzerland",1754000000,1754000000,8.5,47.4,11000.5,false,240.3,180.1]""")]
    // not an array at all
    [InlineData("""{"icao24":"4b1806"}""")]
    [InlineData("""42""")]
    // icao24 missing, null, or blank - it keys the in-memory dictionaries
    [InlineData("""[null,"SWR123","Switzerland",1754000000,1754000000,8.5,47.4,11000.5,false,240.3,180.1,0.0]""")]
    [InlineData("""["   ","SWR123","Switzerland",1754000000,1754000000,8.5,47.4,11000.5,false,240.3,180.1,0.0]""")]
    // last_contact, longitude or latitude not a number
    [InlineData("""["4b1806","SWR123","Switzerland",1754000000,null,8.5,47.4,11000.5,false,240.3,180.1,0.0]""")]
    [InlineData("""["4b1806","SWR123","Switzerland",1754000000,1754000000,null,47.4,11000.5,false,240.3,180.1,0.0]""")]
    [InlineData("""["4b1806","SWR123","Switzerland",1754000000,1754000000,8.5,null,11000.5,false,240.3,180.1,0.0]""")]
    public void Unusable_rows_are_skipped(string row)
    {
        var result = OpenSkyStateParser.Parse(StatesJson(row));

        Assert.Empty(result.Aircraft);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void Null_on_ground_is_treated_as_airborne()
    {
        // GetBoolean() threw on null here. Null means "not reported", and the
        // ingestion path only tracks airborne aircraft, so the default matters.
        var result = OpenSkyStateParser.Parse(StatesJson(
            """["4b1806","SWR123","Switzerland",1754000000,1754000000,8.5,47.4,11000.5,null,240.3,180.1,0.0]"""));

        Assert.False(Assert.Single(result.Aircraft).OnGround);
    }

    [Fact]
    public void On_ground_true_is_preserved()
    {
        var result = OpenSkyStateParser.Parse(StatesJson(
            """["4b1806","SWR123","Switzerland",1754000000,1754000000,8.5,47.4,11000.5,true,240.3,180.1,0.0]"""));

        Assert.True(Assert.Single(result.Aircraft).OnGround);
    }

    [Fact]
    public void Null_optional_fields_are_tolerated_and_the_row_is_kept()
    {
        var result = OpenSkyStateParser.Parse(StatesJson(
            """["4b1806",null,null,1754000000,1754000000,8.5,47.4,null,false,null,null,null]"""));

        var aircraft = Assert.Single(result.Aircraft);
        Assert.Null(aircraft.Callsign);
        Assert.Null(aircraft.OriginCountry);
        Assert.Null(aircraft.BarometricAltitude);
        Assert.Null(aircraft.Velocity);
        Assert.Null(aircraft.Heading);
        Assert.Null(aircraft.VerticalRate);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void Null_states_yields_no_aircraft()
    {
        // OpenSky sends this when nothing matches the query.
        var result = OpenSkyStateParser.Parse("""{"time":1754000000,"states":null}""");

        Assert.Empty(result.Aircraft);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void Missing_states_property_yields_no_aircraft()
    {
        var result = OpenSkyStateParser.Parse("""{"time":1754000000}""");

        Assert.Empty(result.Aircraft);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void Empty_states_array_yields_no_aircraft()
    {
        var result = OpenSkyStateParser.Parse("""{"time":1754000000,"states":[]}""");

        Assert.Empty(result.Aircraft);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void A_states_property_that_is_neither_null_nor_an_array_throws()
    {
        // Deliberate: a malformed body must fail the poll rather than quietly
        // report zero aircraft, which would blank the map and still look healthy.
        Assert.Throws<InvalidOperationException>(() =>
            OpenSkyStateParser.Parse("""{"time":1754000000,"states":{"unexpected":true}}"""));
    }
}
