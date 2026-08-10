"use client";

import dynamic from "next/dynamic";

const FlightMap = dynamic(() => import("@/components/map/FlightMap"), {
  ssr: false,
  loading: () => (
    <div className="w-full h-dvh bg-app flex items-center justify-center">
      <p className="text-gray-400 text-[13px]">Loading map…</p>
    </div>
  ),
});

export default function Home() {
  return (
    <main className="w-full h-dvh">
      <FlightMap />
    </main>
  );
}
