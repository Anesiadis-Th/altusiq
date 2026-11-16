import { useQuery } from "@tanstack/react-query";
import { AnomalyEventDetail, AnomalyEventSummary } from "@/types/event";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "";

export function useEvents() {
  return useQuery<AnomalyEventSummary[]>({
    queryKey: ["events"],
    queryFn: async () => {
      const res = await fetch(`${API_URL}/api/events`);
      if (!res.ok) throw new Error("Failed to fetch events");
      return res.json();
    },
    refetchInterval: 60_000,
    staleTime: 30_000,
  });
}

export function useEvent(id: string | null) {
  return useQuery<AnomalyEventDetail>({
    queryKey: ["event", id],
    queryFn: async () => {
      const res = await fetch(`${API_URL}/api/events/${id}`);
      if (!res.ok) throw new Error("Failed to fetch event");
      return res.json();
    },
    enabled: id !== null,
    // A brief can land minutes after the event closes.
    refetchInterval: 60_000,
    staleTime: 30_000,
  });
}
