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
