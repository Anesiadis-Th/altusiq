import { useQuery, type QueryClient } from "@tanstack/react-query";
import { AIRPORT_CODES_URL, type AirportCodes } from "@/lib/airports";

const queryKey = ["airport-codes"] as const;

async function fetchAirportCodes(): Promise<AirportCodes> {
  const res = await fetch(AIRPORT_CODES_URL);
  if (!res.ok) throw new Error("Failed to fetch airport codes");
  return res.json();
}

// One retry, not the default three. The UI falls back to raw ICAO codes anyway,
// and three backoffs would hold the panel on its skeleton for all of them.
const options = {
  queryKey,
  queryFn: fetchAirportCodes,
  staleTime: Infinity,
  gcTime: Infinity,
  retry: 1,
} as const;

export function useAirportCodes() {
  return useQuery<AirportCodes>(options);
}

export function prefetchAirportCodes(client: QueryClient): void {
  void client.prefetchQuery(options);
}
