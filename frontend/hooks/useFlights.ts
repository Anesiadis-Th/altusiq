import { useQuery } from "@tanstack/react-query";
import { FlightSummary, FlightTrack } from "@/types/flight";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "";

export function useFlights() {
  return useQuery<FlightSummary[]>({
    queryKey: ["flights"],
    queryFn: async () => {
      const res = await fetch(`${API_URL}/api/flights`);
      if (!res.ok) throw new Error("Failed to fetch flights");
      return res.json();
    },
    refetchInterval: 30_000,
    staleTime: 15_000,
  });
}

export function useFlightTrack(id: string | null) {
  return useQuery<FlightTrack>({
    queryKey: ["flight-track", id],
    queryFn: async () => {
      const res = await fetch(`${API_URL}/api/flights/${id}/track`);
      if (!res.ok) throw new Error("Failed to fetch track");
      return res.json();
    },
    enabled: id !== null,
    staleTime: Infinity,
  });
}

export function useActiveTrack(icao24: string | null) {
  return useQuery<FlightTrack | null>({
    queryKey: ["active-track", icao24],
    queryFn: async () => {
      const res = await fetch(`${API_URL}/api/flights/active/${icao24}/track`);
      if (res.status === 404) return null;
      if (!res.ok) throw new Error("Failed to fetch active track");
      return res.json();
    },
    enabled: icao24 !== null,
    refetchInterval: 30_000,
    staleTime: 15_000,
  });
}
