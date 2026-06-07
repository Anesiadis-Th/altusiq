"use client";

import dynamic from "next/dynamic";

const FlightMap = dynamic(() => import("@/components/map/FlightMap"), {
  ssr: false,
  loading: () => (
    <div className="w-full h-screen bg-gray-950 flex items-center justify-center">
      <p className="text-gray-400">Loading map...</p>
    </div>
  ),
});

export default function Home() {
  return (
    <main className="w-full h-screen">
      <FlightMap />
    </main>
  );
}
