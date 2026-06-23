import { Aircraft } from "@/types/aircraft";

const METERS_PER_DEGREE_LAT = 111_320;
const DEG_TO_RAD = Math.PI / 180;

const CORRECTION_MS = 1_500;
const MAX_EXTRAPOLATE_S = 150;

export interface SampledAircraft {
  icao24: string;
  longitude: number;
  latitude: number;
  heading: number | null;
}

interface Track {
  icao24: string;
  heading: number | null;
  velocity: number | null;
  onGround: boolean;
  baseLon: number;
  baseLat: number;
  baseTimeMs: number;
  offsetLon: number;
  offsetLat: number;
  offsetTimeMs: number;
}

export function advance(
  lon: number,
  lat: number,
  velocity: number,
  heading: number,
  dtSeconds: number,
): { lon: number; lat: number } {
  const distance = velocity * dtSeconds;
  const theta = heading * DEG_TO_RAD;
  const dLat = (distance * Math.cos(theta)) / METERS_PER_DEGREE_LAT;
  const cosLat = Math.cos(lat * DEG_TO_RAD);
  const dLon =
    cosLat === 0
      ? 0
      : (distance * Math.sin(theta)) / (METERS_PER_DEGREE_LAT * cosLat);
  return { lon: lon + dLon, lat: lat + dLat };
}

function deadReckon(track: Track, nowMs: number): { lon: number; lat: number } {
  if (
    track.onGround ||
    track.velocity == null ||
    track.velocity <= 0 ||
    track.heading == null
  ) {
    return { lon: track.baseLon, lat: track.baseLat };
  }
  const dtSeconds = Math.min(
    (nowMs - track.baseTimeMs) / 1000,
    MAX_EXTRAPOLATE_S,
  );
  return advance(
    track.baseLon,
    track.baseLat,
    track.velocity,
    track.heading,
    dtSeconds,
  );
}

export class DeadReckoningEngine {
  private tracks = new Map<string, Track>();

  ingest(aircraft: Aircraft[], nowMs: number): void {
    const seen = new Set<string>();

    for (const a of aircraft) {
      if (a.longitude == null || a.latitude == null) continue;
      seen.add(a.icao24);

      const existing = this.tracks.get(a.icao24);
      if (existing) {
        const rendered = this.renderPosition(existing, nowMs);
        existing.heading = a.heading;
        existing.velocity = a.velocity;
        existing.onGround = a.on_ground;
        existing.baseLon = a.longitude;
        existing.baseLat = a.latitude;
        existing.baseTimeMs = nowMs;
        existing.offsetLon = rendered.lon - a.longitude;
        existing.offsetLat = rendered.lat - a.latitude;
        existing.offsetTimeMs = nowMs;
      } else {
        this.tracks.set(a.icao24, {
          icao24: a.icao24,
          heading: a.heading,
          velocity: a.velocity,
          onGround: a.on_ground,
          baseLon: a.longitude,
          baseLat: a.latitude,
          baseTimeMs: nowMs,
          offsetLon: 0,
          offsetLat: 0,
          offsetTimeMs: nowMs,
        });
      }
    }

    for (const icao24 of this.tracks.keys()) {
      if (!seen.has(icao24)) this.tracks.delete(icao24);
    }
  }

  sample(nowMs: number): SampledAircraft[] {
    const result: SampledAircraft[] = [];
    for (const track of this.tracks.values()) {
      if (track.onGround) continue;
      const position = this.renderPosition(track, nowMs);
      result.push({
        icao24: track.icao24,
        longitude: position.lon,
        latitude: position.lat,
        heading: track.heading,
      });
    }
    return result;
  }

  private renderPosition(
    track: Track,
    nowMs: number,
  ): { lon: number; lat: number } {
    const base = deadReckon(track, nowMs);
    const blend = Math.min((nowMs - track.offsetTimeMs) / CORRECTION_MS, 1);
    const decay = 1 - blend;
    return {
      lon: base.lon + track.offsetLon * decay,
      lat: base.lat + track.offsetLat * decay,
    };
  }
}
