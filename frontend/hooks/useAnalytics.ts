import { useQuery, type QueryClient } from "@tanstack/react-query";
import { Analytics } from "@/types/analytics";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "";

const queryKey = ["analytics"] as const;
const staleTime = 5 * 60_000;

async function fetchAnalytics(): Promise<Analytics> {
  const res = await fetch(`${API_URL}/api/analytics`);
  if (!res.ok) throw new Error("Failed to fetch analytics");
  return res.json();
}

export function useAnalytics() {
  return useQuery<Analytics>({ queryKey, queryFn: fetchAnalytics, staleTime });
}

// Called on hover, because the overlay is a lazy chunk and the request would
// otherwise wait for it to download and mount first.
export function prefetchAnalytics(client: QueryClient): void {
  void client.prefetchQuery({ queryKey, queryFn: fetchAnalytics, staleTime });
}
