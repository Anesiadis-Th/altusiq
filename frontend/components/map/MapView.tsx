"use client";

import { useEffect, useRef, useState } from "react";
import mapboxgl from "mapbox-gl";
import "mapbox-gl/dist/mapbox-gl.css";
import { Aircraft } from "@/types/aircraft";
import FlightPanel from "./FlightPanel";

interface MapViewProps {
  aircraft: Aircraft[];
  connected: boolean;
}

export default function MapView({ aircraft, connected }: MapViewProps) {
  const mapContainer = useRef<HTMLDivElement>(null);
  const map = useRef<mapboxgl.Map | null>(null);
  const markers = useRef<Map<string, mapboxgl.Marker>>(new Map());
  const [selectedIcao, setSelectedIcao] = useState<string | null>(null);
  const selected = aircraft.find((a) => a.icao24 === selectedIcao) ?? null;

  useEffect(() => {
    if (map.current || !mapContainer.current) return;

    mapboxgl.accessToken = process.env.NEXT_PUBLIC_MAPBOX_TOKEN!;

    map.current = new mapboxgl.Map({
      container: mapContainer.current,
      style: "mapbox://styles/mapbox/dark-v11",
      center: [15, 50],
      zoom: 4,
    });

    map.current.addControl(new mapboxgl.NavigationControl(), "top-right");
  }, []);

  useEffect(() => {
    if (!map.current) return;

    const currentIcaos = new Set(aircraft.map((a) => a.icao24));

    markers.current.forEach((marker, icao24) => {
      if (!currentIcaos.has(icao24)) {
        marker.remove();
        markers.current.delete(icao24);
      }
    });

    aircraft.forEach((plane) => {
      if (plane.on_ground) return;

      const existing = markers.current.get(plane.icao24);

      if (existing) {
        existing.setLngLat([plane.longitude, plane.latitude]);
        // Update heading rotation on existing marker
        const el = existing.getElement();
        el.style.transform = `rotate(${(plane.heading ?? 0) - 90}deg)`;
      } else {
        const el = createMarkerElement(plane.heading);
        el.addEventListener("click", () => setSelectedIcao(plane.icao24));

        const marker = new mapboxgl.Marker({ element: el })
          .setLngLat([plane.longitude, plane.latitude])
          .addTo(map.current!);

        markers.current.set(plane.icao24, marker);
      }
    });
  }, [aircraft]);

  return (
    <div className="relative w-full h-full">
      <div ref={mapContainer} className="w-full h-full" />
      <StatusBadge connected={connected} count={aircraft.length} />
      <FlightPanel aircraft={selected} onClose={() => setSelectedIcao(null)} />
    </div>
  );
}

function createMarkerElement(heading: number | null): HTMLElement {
  const el = document.createElement("div");
  el.style.cssText = `
    width: 24px;
    height: 24px;
    cursor: pointer;
    transform: rotate(${(heading ?? 0) - 90}deg);
    transition: transform 0.5s ease;
    filter: drop-shadow(0 0 3px rgba(99, 179, 237, 0.6));
  `;

  el.innerHTML = `
    <svg viewBox="0 0 24 24" fill="none" xmlns="http://www.w3.org/2000/svg">
      <path
        d="M21 16V14L13 9V3.5C13 2.67 12.33 2 11.5 2C10.67 2 10 2.67 10 3.5V9L2 14V16L10 13.5V19L8 20.5V22L11.5 21L15 22V20.5L13 19V13.5L21 16Z"
        fill="#63b3ed"
      />
    </svg>
  `;

  return el;
}

function StatusBadge({
  connected,
  count,
}: {
  connected: boolean;
  count: number;
}) {
  return (
    <div className="absolute top-4 left-4 z-10 bg-gray-900 bg-opacity-90 rounded-lg px-3 py-2 text-sm flex items-center gap-2">
      <span
        className={`w-2 h-2 rounded-full ${
          connected ? "bg-green-400" : "bg-red-400"
        }`}
      />
      <span className="text-white">
        {connected ? `${count.toLocaleString()} aircraft` : "Connecting..."}
      </span>
    </div>
  );
}
