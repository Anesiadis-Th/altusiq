import { useQuery } from "@tanstack/react-query";
import { FlightRoute } from "@/types/route";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "";

export function useRoute(callsign: string | null) {
  const trimmed = callsign?.trim() || null;

  return useQuery<FlightRoute | null>({
    queryKey: ["route", trimmed],
    enabled: trimmed !== null,
    staleTime: 60 * 60_000,
    retry: false,
    queryFn: async () => {
      const res = await fetch(`${API_URL}/api/routes/${trimmed}`);
      if (res.status === 404) return null;
      if (!res.ok) throw new Error("Failed to fetch route");
      return res.json();
    },
  });
}
