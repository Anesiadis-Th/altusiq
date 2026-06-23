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

export function startAircraftRenderLoop(
  getSource: () => AircraftSource | undefined,
  engine: DeadReckoningEngine,
  raf: (cb: FrameRequestCallback) => number = requestAnimationFrame,
  caf: (handle: number) => void = cancelAnimationFrame,
  now: () => number = () => performance.now(),
): () => void {
  let handle = raf(function loop(): void {
    getSource()?.setData(toFeatureCollection(engine.sample(now())));
    handle = raf(loop);
  });
  return () => caf(handle);
}
