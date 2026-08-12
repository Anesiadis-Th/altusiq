import { describe, expect, it } from "vitest";
import { DeadReckoningEngine, advance } from "./deadReckoning";
import { Aircraft } from "@/types/aircraft";

const METERS_PER_DEGREE_LAT = 111_320;

// Mirrors the constants in deadReckoning.ts. Duplicated deliberately: if
// someone changes them there, these tests should fail and force a decision.
const CORRECTION_MS = 1_500;
const MAX_EXTRAPOLATE_S = 150;

function plane(overrides: Partial<Aircraft> & { icao24: string }): Aircraft {
  return {
    callsign: "SAS4787",
    origin_country: "Sweden",
    longitude: 10,
    latitude: 60,
    barometric_altitude: 9000,
    on_ground: false,
    velocity: 200,
    heading: 0,
    vertical_rate: 0,
    last_contact: 1_700_000_000,
    category: null,
    ...overrides,
  };
}

describe("advance", () => {
  it("moves due north on heading 0", () => {
    const { lon, lat } = advance(10, 60, 100, 0, 10);
    expect(lon).toBeCloseTo(10, 10);
    expect(lat).toBeCloseTo(60 + 1000 / METERS_PER_DEGREE_LAT, 10);
  });

  it("moves due east on heading 90", () => {
    const { lon, lat } = advance(10, 0, 100, 90, 10);
    // At the equator a degree of longitude is a full degree of latitude.
    expect(lon).toBeCloseTo(10 + 1000 / METERS_PER_DEGREE_LAT, 10);
    expect(lat).toBeCloseTo(0, 10);
  });

  it("compresses longitude with latitude", () => {
    const equator = advance(10, 0, 100, 90, 10);
    const north = advance(10, 60, 100, 90, 10);
    // cos(60°) = 0.5, so the same distance spans twice the longitude.
    expect(north.lon - 10).toBeCloseTo((equator.lon - 10) / Math.cos(Math.PI / 3), 6);
  });
});

describe("DeadReckoningEngine", () => {
  it("returns a newly ingested aircraft at its reported position", () => {
    const engine = new DeadReckoningEngine();
    engine.ingest([plane({ icao24: "abc123" })], 0);

    const [sampled] = engine.sample(0);
    expect(sampled.icao24).toBe("abc123");
    expect(sampled.longitude).toBeCloseTo(10, 10);
    expect(sampled.latitude).toBeCloseTo(60, 10);
  });

  it("extrapolates forward between polls", () => {
    const engine = new DeadReckoningEngine();
    engine.ingest([plane({ icao24: "abc123", velocity: 100, heading: 0 })], 0);

    const [sampled] = engine.sample(10_000);
    expect(sampled.latitude).toBeCloseTo(60 + 1000 / METERS_PER_DEGREE_LAT, 8);
  });

  it("freezes aircraft reported on the ground", () => {
    const engine = new DeadReckoningEngine();
    engine.ingest([plane({ icao24: "abc123", on_ground: true })], 0);

    // Grounded aircraft are not drawn at all.
    expect(engine.sample(60_000)).toEqual([]);
    // ...but they do not move either, so a direct lookup stays put.
    expect(engine.position("abc123", 60_000)).toEqual({
      longitude: 10,
      latitude: 60,
    });
  });

  it("ignores aircraft with no position", () => {
    const engine = new DeadReckoningEngine();
    engine.ingest(
      [
        plane({
          icao24: "abc123",
          longitude: null as unknown as number,
        }),
      ],
      0,
    );
    expect(engine.sample(0)).toEqual([]);
  });

  describe("correction blend", () => {
    // Velocity null isolates the blend from forward motion.
    function engineWithJump() {
      const engine = new DeadReckoningEngine();
      engine.ingest(
        [plane({ icao24: "abc123", velocity: null, longitude: 10 })],
        0,
      );
      engine.ingest(
        [plane({ icao24: "abc123", velocity: null, longitude: 10.5 })],
        1_000,
      );
      return engine;
    }

    it("does not teleport when a new fix arrives", () => {
      // Still rendered at the old position the instant the fix lands.
      expect(engineWithJump().position("abc123", 1_000)?.longitude).toBeCloseTo(
        10,
        10,
      );
    });

    it("is halfway across at half the correction window", () => {
      expect(
        engineWithJump().position("abc123", 1_000 + CORRECTION_MS / 2)
          ?.longitude,
      ).toBeCloseTo(10.25, 10);
    });

    it("has fully converged once the window closes", () => {
      expect(
        engineWithJump().position("abc123", 1_000 + CORRECTION_MS)?.longitude,
      ).toBeCloseTo(10.5, 10);
    });
  });

  describe("missed-poll tolerance and eviction", () => {
    function engineWithOneTrack() {
      const engine = new DeadReckoningEngine();
      engine.ingest([plane({ icao24: "abc123" })], 0);
      return engine;
    }

    it("survives a single missed 120s poll", () => {
      // The frontend mirror of the gap-vs-poll-interval rule: one absent
      // poll must not flicker the aircraft off the map.
      expect(engineWithOneTrack().sample(120_000)).toHaveLength(1);
    });

    it("survives right up to the extrapolation limit", () => {
      expect(
        engineWithOneTrack().sample(MAX_EXTRAPOLATE_S * 1_000),
      ).toHaveLength(1);
    });

    it("evicts once unseen longer than the limit", () => {
      expect(
        engineWithOneTrack().sample(MAX_EXTRAPOLATE_S * 1_000 + 1),
      ).toEqual([]);
    });

    it("stops reporting a position for an evicted track", () => {
      const engine = engineWithOneTrack();
      expect(engine.position("abc123", MAX_EXTRAPOLATE_S * 1_000 + 1)).toBeNull();
    });

    it("keeps a track alive when it is seen again", () => {
      const engine = engineWithOneTrack();
      engine.ingest([plane({ icao24: "abc123" })], 120_000);
      // The clock restarts from the new fix, not from first sight.
      expect(engine.sample(240_000)).toHaveLength(1);
    });

    it("returns null for an aircraft it has never seen", () => {
      expect(engineWithOneTrack().position("nope99", 0)).toBeNull();
    });
  });
});
