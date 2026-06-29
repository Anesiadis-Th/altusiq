"use client";

import dynamic from "next/dynamic";

const AnalyticsDashboard = dynamic(
  () => import("@/components/analytics/AnalyticsDashboard"),
  {
    ssr: false,
    loading: () => (
      <div className="w-full min-h-screen bg-gray-950 flex items-center justify-center">
        <p className="text-gray-400">Loading analytics…</p>
      </div>
    ),
  },
);

export default function AnalyticsPage() {
  return (
    <main className="w-full min-h-screen bg-gray-950">
      <AnalyticsDashboard />
    </main>
  );
}
