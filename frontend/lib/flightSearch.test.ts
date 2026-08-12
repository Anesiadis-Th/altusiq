import { describe, expect, it } from "vitest";
import { searchAircraft } from "./flightSearch";
import { Aircraft } from "@/types/aircraft";

function plane(overrides: Partial<Aircraft> & { icao24: string }): Aircraft {
  return {
    callsign: null,
    origin_country: "Sweden",
    longitude: 10,
    latitude: 60,
    barometric_altitude: 9000,
    on_ground: false,
    velocity: 230,
    heading: 180,
    vertical_rate: 0,
    last_contact: 1_700_000_000,
    ...overrides,
  };
}

const labels = (results: { label: string }[]) => results.map((r) => r.label);

describe("searchAircraft", () => {
  it("returns nothing for queries shorter than two characters", () => {
    const fleet = [plane({ icao24: "4ca1fa", callsign: "SAS4787" })];
    expect(searchAircraft(fleet, "S")).toEqual([]);
    expect(searchAircraft(fleet, "")).toEqual([]);
  });

  it("matches a callsign directly", () => {
    const fleet = [plane({ icao24: "4ca1fa", callsign: "SAS4787" })];
    expect(labels(searchAircraft(fleet, "SAS4787"))).toEqual(["SAS4787"]);
  });

  it("translates an IATA flight number to its ICAO callsign", () => {
    const fleet = [plane({ icao24: "4ca1fa", callsign: "SAS4787" })];
    // SK is Scandinavian; the boarding pass says SK4787, the radio says SAS4787.
    expect(labels(searchAircraft(fleet, "SK4787"))).toEqual(["SAS4787"]);
  });

  it("strips leading zeros from the flight number", () => {
    const fleet = [plane({ icao24: "4ca1fa", callsign: "SAS34" })];
    // SK0034 flies as SAS34, not SAS0034.
    expect(labels(searchAircraft(fleet, "SK0034"))).toEqual(["SAS34"]);
  });

  it("still matches when the zeros are not dropped", () => {
    const fleet = [plane({ icao24: "4ca1fa", callsign: "SAS0034" })];
    expect(labels(searchAircraft(fleet, "SK0034"))).toEqual(["SAS0034"]);
  });

  it("follows every ICAO prefix of a one-to-many airline code", () => {
    const fleet = [
      plane({ icao24: "3c6444", callsign: "DLH400" }),
      plane({ icao24: "3c4b26", callsign: "GEC8260" }),
    ];
    // LH maps to both DLH (passenger) and GEC (Lufthansa Cargo).
    expect(labels(searchAircraft(fleet, "LH400"))).toEqual(["DLH400"]);
    expect(labels(searchAircraft(fleet, "LH8260"))).toEqual(["GEC8260"]);
  });

  it("is case and whitespace insensitive", () => {
    const fleet = [plane({ icao24: "4ca1fa", callsign: "SAS4787" })];
    expect(labels(searchAircraft(fleet, "  sas 4787 "))).toEqual(["SAS4787"]);
  });

  it("excludes grounded aircraft", () => {
    const fleet = [
      plane({ icao24: "4ca1fa", callsign: "SAS4787", on_ground: true }),
    ];
    // The render loop does not draw grounded planes, so flying the camera
    // to one would land on nothing.
    expect(searchAircraft(fleet, "SAS4787")).toEqual([]);
  });

  it("ranks exact above prefix, prefix above substring", () => {
    const fleet = [
      plane({ icao24: "000003", callsign: "XXSAS40" }), // substring
      plane({ icao24: "000002", callsign: "SAS4040" }), // prefix
      plane({ icao24: "000001", callsign: "SAS40" }), // exact
    ];
    expect(labels(searchAircraft(fleet, "SAS40"))).toEqual([
      "SAS40",
      "SAS4040",
      "XXSAS40",
    ]);
  });

  it("matches an icao24 hex prefix and ranks it last", () => {
    const fleet = [
      plane({ icao24: "4ca1fa", callsign: null }),
      plane({ icao24: "000001", callsign: "4CA1FA99" }),
    ];
    const results = searchAircraft(fleet, "4CA1FA");
    // The callsign match outranks the hex match, which falls back to its hex label.
    expect(labels(results)).toEqual(["4CA1FA99", "4CA1FA"]);
  });

  it("sorts ties alphabetically so ordering is stable", () => {
    const fleet = [
      plane({ icao24: "000002", callsign: "SAS99" }),
      plane({ icao24: "000001", callsign: "SAS11" }),
    ];
    expect(labels(searchAircraft(fleet, "SAS"))).toEqual(["SAS11", "SAS99"]);
  });

  it("honours the result limit", () => {
    const fleet = Array.from({ length: 20 }, (_, i) =>
      plane({
        icao24: String(i).padStart(6, "0"),
        callsign: `SAS${String(i).padStart(3, "0")}`,
      }),
    );
    expect(searchAircraft(fleet, "SAS")).toHaveLength(8);
    expect(searchAircraft(fleet, "SAS", 3)).toHaveLength(3);
  });

  it("ignores aircraft with no callsign when the query is not hex", () => {
    const fleet = [plane({ icao24: "4ca1fa", callsign: null })];
    expect(searchAircraft(fleet, "SASZZ")).toEqual([]);
  });
});
