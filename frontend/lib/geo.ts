import { FlightRoute } from "@/types/route";

const EARTH_RADIUS_KM = 6371;
const ROUTE_DETOUR_FACTOR = 1.5;
const ROUTE_DETOUR_SLACK_KM = 100;

export function haversineKm(
  lat1: number,
  lon1: number,
  lat2: number,
  lon2: number,
): number {
  const toRad = (deg: number) => (deg * Math.PI) / 180;
  const dLat = toRad(lat2 - lat1);
  const dLon = toRad(lon2 - lon1);
  const a =
    Math.sin(dLat / 2) ** 2 +
    Math.cos(toRad(lat1)) * Math.cos(toRad(lat2)) * Math.sin(dLon / 2) ** 2;
  return 2 * EARTH_RADIUS_KM * Math.asin(Math.sqrt(a));
}

// adsbdb's callsign table goes stale when airlines reassign flight numbers, so
// drop a route if the aircraft sits nowhere near the line between its airports.
// The tolerance is generous on purpose: SID turns and holds must not trip it.
export function isRoutePlausible(
  route: FlightRoute,
  aircraftLat: number,
  aircraftLon: number,
): boolean {
  const { origin, destination } = route;
  if (
    origin.latitude == null ||
    origin.longitude == null ||
    destination.latitude == null ||
    destination.longitude == null
  ) {
    return true;
  }
  const directKm = haversineKm(
    origin.latitude,
    origin.longitude,
    destination.latitude,
    destination.longitude,
  );
  const viaAircraftKm =
    haversineKm(origin.latitude, origin.longitude, aircraftLat, aircraftLon) +
    haversineKm(aircraftLat, aircraftLon, destination.latitude, destination.longitude);
  return viaAircraftKm <= directKm * ROUTE_DETOUR_FACTOR + ROUTE_DETOUR_SLACK_KM;
}
