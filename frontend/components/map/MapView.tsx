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

interface AircraftFeatureProps {
  icao24: string;
  heading: number | null;
}

const AIRCRAFT_SVG = `<svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24"><path d="M21 16V14L13 9V3.5C13 2.67 12.33 2 11.5 2C10.67 2 10 2.67 10 3.5V9L2 14V16L10 13.5V19L8 20.5V22L11.5 21L15 22V20.5L13 19V13.5L21 16Z" fill="#63b3ed"/></svg>`;

const rotateExpression: mapboxgl.ExpressionSpecification = [
  "coalesce",
  ["get", "heading"],
  0,
];

function toFeatureCollection(
  aircraft: Aircraft[],
): GeoJSON.FeatureCollection<GeoJSON.Point, AircraftFeatureProps> {
  return {
    type: "FeatureCollection",
    features: aircraft
      .filter((a) => !a.on_ground && a.longitude != null && a.latitude != null)
      .map((a) => ({
        type: "Feature",
        geometry: {
          type: "Point",
          coordinates: [a.longitude, a.latitude],
        },
        properties: { icao24: a.icao24, heading: a.heading },
      })),
  };
}

async function loadAircraftIcon(map: mapboxgl.Map): Promise<void> {
  if (map.hasImage("aircraft")) return;

  const image = new Image(48, 48);
  const source = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(AIRCRAFT_SVG)}`;

  await new Promise<void>((resolve, reject) => {
    image.onload = () => resolve();
    image.onerror = () => reject(new Error("Failed to load aircraft icon"));
    image.src = source;
  });

  map.addImage("aircraft", image, { pixelRatio: 2 });
}

export default function MapView({ aircraft, connected }: MapViewProps) {
  const mapContainer = useRef<HTMLDivElement>(null);
  const map = useRef<mapboxgl.Map | null>(null);
  const [ready, setReady] = useState(false);
  const [selectedIcao, setSelectedIcao] = useState<string | null>(null);

  useEffect(() => {
    if (map.current || !mapContainer.current) return;

    mapboxgl.accessToken = process.env.NEXT_PUBLIC_MAPBOX_TOKEN!;

    const instance = new mapboxgl.Map({
      container: mapContainer.current,
      style: "mapbox://styles/mapbox/dark-v11",
      center: [15, 50],
      zoom: 4,
    });

    instance.addControl(new mapboxgl.NavigationControl(), "top-right");

    instance.on("load", async () => {
      await loadAircraftIcon(instance);

      instance.addSource("aircraft", {
        type: "geojson",
        data: { type: "FeatureCollection", features: [] },
      });

      instance.addLayer({
        id: "aircraft-layer",
        type: "symbol",
        source: "aircraft",
        layout: {
          "icon-image": "aircraft",
          "icon-allow-overlap": true,
          "icon-rotation-alignment": "map",
          "icon-rotate": rotateExpression,
        },
      });

      instance.addLayer(
        {
          id: "aircraft-highlight",
          type: "circle",
          source: "aircraft",
          filter: ["==", ["get", "icao24"], ""],
          paint: {
            "circle-radius": 18,
            "circle-color": "#63b3ed",
            "circle-opacity": 0.16,
            "circle-stroke-color": "#63b3ed",
            "circle-stroke-width": 1.5,
            "circle-stroke-opacity": 0.7,
          },
        },
        "aircraft-layer",
      );

      instance.on("click", "aircraft-layer", (event) => {
        const feature = event.features?.[0];
        if (!feature) return;
        const properties = feature.properties as AircraftFeatureProps | null;
        if (!properties) return;
        setSelectedIcao(properties.icao24);
      });

      instance.on("mouseenter", "aircraft-layer", () => {
        instance.getCanvas().style.cursor = "pointer";
      });

      instance.on("mouseleave", "aircraft-layer", () => {
        instance.getCanvas().style.cursor = "";
      });

      setReady(true);
    });

    map.current = instance;

    return () => {
      instance.remove();
      map.current = null;
      setReady(false);
    };
  }, []);

  useEffect(() => {
    if (!map.current || !ready) return;
    const source = map.current.getSource("aircraft") as
      | mapboxgl.GeoJSONSource
      | undefined;
    source?.setData(toFeatureCollection(aircraft));
  }, [aircraft, ready]);

  useEffect(() => {
    const instance = map.current;
    if (!instance || !ready) return;
    instance.setFilter("aircraft-highlight", [
      "==",
      ["get", "icao24"],
      selectedIcao ?? "",
    ]);
  }, [selectedIcao, ready]);

  const selected = selectedIcao
    ? (aircraft.find((a) => a.icao24 === selectedIcao) ?? null)
    : null;

  return (
    <div className="relative w-full h-full">
      <div ref={mapContainer} className="w-full h-full" />
      <StatusBadge connected={connected} count={aircraft.length} />
      <FlightPanel aircraft={selected} onClose={() => setSelectedIcao(null)} />
    </div>
  );
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
