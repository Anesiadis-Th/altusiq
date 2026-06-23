import { DeadReckoningEngine, SampledAircraft } from "@/lib/deadReckoning";

export interface AircraftFeatureProps {
  icao24: string;
  heading: number | null;
}

export type AircraftFeatureCollection = GeoJSON.FeatureCollection<
  GeoJSON.Point,
  AircraftFeatureProps
>;

export interface AircraftSource {
  setData(data: AircraftFeatureCollection): void;
}

export function toFeatureCollection(
  sampled: SampledAircraft[],
): AircraftFeatureCollection {
  return {
    type: "FeatureCollection",
    features: sampled.map((a) => ({
      type: "Feature",
      geometry: { type: "Point", coordinates: [a.longitude, a.latitude] },
      properties: { icao24: a.icao24, heading: a.heading },
    })),
  };
}

// Dead-reckoned aircraft move well under a pixel per frame, so rebuilding the
// whole GeoJSON source at 60fps is wasted work that saturates the main thread.
// Redraw at ~10fps instead — visually identical, far cheaper.
const RENDER_INTERVAL_MS = 100;

export function startAircraftRenderLoop(
  getSource: () => AircraftSource | undefined,
  engine: DeadReckoningEngine,
  raf: (cb: FrameRequestCallback) => number = requestAnimationFrame,
  caf: (handle: number) => void = cancelAnimationFrame,
  now: () => number = () => performance.now(),
  intervalMs: number = RENDER_INTERVAL_MS,
): () => void {
  let lastDraw = Number.NEGATIVE_INFINITY;
  let handle = raf(function loop(): void {
    const t = now();
    if (t - lastDraw >= intervalMs) {
      lastDraw = t;
      getSource()?.setData(toFeatureCollection(engine.sample(t)));
    }
    handle = raf(loop);
  });
  return () => caf(handle);
}
