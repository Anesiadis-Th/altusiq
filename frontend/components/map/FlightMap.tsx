"use client";

import { useFlightData } from "@/hooks/useFlightData";
import MapView from "./MapView";

export default function FlightMap() {
  const { aircraft, connected, error } = useFlightData();

  if (error) {
    return (
      <div className="w-full h-screen bg-gray-950 flex items-center justify-center">
        <p className="text-red-400">Connection error: {error}</p>
      </div>
    );
  }

  return <MapView aircraft={aircraft} connected={connected} />;
}
