import { useQuery } from "@tanstack/react-query";
import { Analytics } from "@/types/analytics";

const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "";

export function useAnalytics() {
  return useQuery<Analytics>({
    queryKey: ["analytics"],
    queryFn: async () => {
      const res = await fetch(`${API_URL}/api/analytics`);
      if (!res.ok) throw new Error("Failed to fetch analytics");
      return res.json();
    },
    staleTime: 5 * 60_000,
  });
}
