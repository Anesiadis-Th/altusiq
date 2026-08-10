import type { Metadata } from "next";
import AnalyticsDashboard from "@/components/analytics/AnalyticsDashboard";

export const metadata: Metadata = {
  title: "Analytics · AltusIQ",
  description: "Flight analytics over the last 15 days",
};

export default function AnalyticsPage() {
  return (
    <main className="min-h-dvh bg-app">
      <AnalyticsDashboard />
    </main>
  );
}
