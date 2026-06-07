"use client";

import { useEffect, useRef } from "react";
import mapboxgl from "mapbox-gl";
import "mapbox-gl/dist/mapbox-gl.css";
import { Aircraft } from "@/types/aircraft";

interface MapViewProps {
  aircraft: Aircraft[];
  connected: boolean;
}

export default function MapView({ aircraft, connected }: MapViewProps) {
  const mapContainer = useRef<HTMLDivElement>(null);
  const map = useRef<mapboxgl.Map | null>(null);
  const markers = useRef<Map<string, mapboxgl.Marker>>(new Map());

  // Initialize the map once on mount
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

  // Update markers whenever aircraft data changes
  useEffect(() => {
    if (!map.current) return;

    const currentIcaos = new Set(aircraft.map((a) => a.icao24));

    // Remove markers for aircraft no longer in the data
    markers.current.forEach((marker, icao24) => {
      if (!currentIcaos.has(icao24)) {
        marker.remove();
        markers.current.delete(icao24);
      }
    });

    // Add markers
    aircraft.forEach((plane) => {
      if (plane.on_ground) return;

      const existing = markers.current.get(plane.icao24);

      if (existing) {
        existing.setLngLat([plane.longitude, plane.latitude]);
      } else {
        const el = createMarkerElement(plane.heading);
        const marker = new mapboxgl.Marker({ element: el })
          .setLngLat([plane.longitude, plane.latitude])
          .setPopup(createPopup(plane))
          .addTo(map.current!);

        markers.current.set(plane.icao24, marker);
      }
    });
  }, [aircraft]);

  return (
    <div className="relative w-full h-full">
      <div ref={mapContainer} className="w-full h-full" />
      <StatusBadge connected={connected} count={aircraft.length} />
    </div>
  );
}

function createMarkerElement(heading: number | null): HTMLElement {
  const el = document.createElement("div");
  el.className = "aircraft-marker";
  el.style.cssText = `
    width: 20px;
    height: 20px;
    cursor: pointer;
    transform: rotate(${heading ?? 0}deg);
    font-size: 16px;
    line-height: 1;
    user-select: none;
  `;
  el.textContent = "✈";
  return el;
}

function createPopup(plane: Aircraft): mapboxgl.Popup {
  const altitude = plane.barometric_altitude
    ? `${Math.round(plane.barometric_altitude)}m`
    : "N/A";

  const speed = plane.velocity
    ? `${Math.round(plane.velocity * 3.6)} km/h`
    : "N/A";

  return new mapboxgl.Popup({ offset: 12 }).setHTML(`
    <div style="color:#111;font-family:sans-serif;font-size:13px;line-height:1.6">
      <strong>${plane.callsign ?? plane.icao24}</strong><br/>
      ${plane.origin_country ?? "Unknown"}<br/>
      Altitude: ${altitude}<br/>
      Speed: ${speed}
    </div>
  `);
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
        className={`w-2 h-2 rounded-full ${connected ? "bg-green-400" : "bg-red-400"}`}
      />
      <span className="text-white">
        {connected ? `${count.toLocaleString()} aircraft` : "Connecting..."}
      </span>
    </div>
  );
}
