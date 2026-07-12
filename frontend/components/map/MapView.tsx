"use client";

import { useEffect, useRef, useState } from "react";
import mapboxgl from "mapbox-gl";
import "mapbox-gl/dist/mapbox-gl.css";
import { Aircraft } from "@/types/aircraft";
import { FlightTrack, PlaybackPosition } from "@/types/flight";
import { DeadReckoningEngine } from "@/lib/deadReckoning";
import {
  AircraftFeatureProps,
  AircraftSource,
  startAircraftRenderLoop,
} from "@/lib/aircraftLayer";
import FlightPanel from "./FlightPanel";
import { useActiveTrack } from "@/hooks/useFlights";
import airports from "@/data/airports.json";

interface MapViewProps {
  aircraft: Aircraft[];
  playbackTrack?: FlightTrack | null;
  playbackPosition?: PlaybackPosition | null;
}

const AIRCRAFT_SVG = `<svg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 24 24"><path d="M21 16V14L13 9V3.5C13 2.67 12.33 2 11.5 2C10.67 2 10 2.67 10 3.5V9L2 14V16L10 13.5V19L8 20.5V22L11.5 21L15 22V20.5L13 19V13.5L21 16Z" fill="#63b3ed" stroke="#1a202c" stroke-width="1.5" stroke-linejoin="round"/></svg>`;

const rotateExpression: mapboxgl.ExpressionSpecification = [
  "coalesce",
  ["get", "heading"],
  0,
];

const airportGeoJSON: GeoJSON.FeatureCollection = {
  type: "FeatureCollection",
  features: airports.map((a) => ({
    type: "Feature",
    geometry: { type: "Point", coordinates: [a.lon, a.lat] },
    properties: { iata: a.iata, name: a.name },
  })),
};

function toTrackLineCollection(
  track: FlightTrack | null | undefined,
): GeoJSON.FeatureCollection {
  if (!track || !track.track_points || track.track_points.length < 2) {
    return { type: "FeatureCollection", features: [] };
  }
  return {
    type: "FeatureCollection",
    features: [
      {
        type: "Feature",
        geometry: {
          type: "LineString",
          coordinates: track.track_points.map((p) => [p.longitude, p.latitude]),
        },
        properties: {},
      },
    ],
  };
}

function toLiveTrailCollection(
  track: FlightTrack | null,
  selectedIcao: string,
  head: { longitude: number; latitude: number } | null,
): GeoJSON.FeatureCollection {
  if (!track || track.icao24.toLowerCase() !== selectedIcao.toLowerCase()) {
    return { type: "FeatureCollection", features: [] };
  }
  const coords: [number, number][] = track.track_points.map((p) => [
    p.longitude,
    p.latitude,
  ]);
  if (head) coords.push([head.longitude, head.latitude]);
  if (coords.length < 2) return { type: "FeatureCollection", features: [] };
  return {
    type: "FeatureCollection",
    features: [
      {
        type: "Feature",
        geometry: { type: "LineString", coordinates: coords },
        properties: {},
      },
    ],
  };
}

function toMarkerCollection(
  position: PlaybackPosition | null | undefined,
): GeoJSON.FeatureCollection {
  if (!position) return { type: "FeatureCollection", features: [] };
  return {
    type: "FeatureCollection",
    features: [
      {
        type: "Feature",
        geometry: {
          type: "Point",
          coordinates: [position.longitude, position.latitude],
        },
        properties: {},
      },
    ],
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

export default function MapView({
  aircraft,
  playbackTrack,
  playbackPosition,
}: MapViewProps) {
  const mapContainer = useRef<HTMLDivElement>(null);
  const map = useRef<mapboxgl.Map | null>(null);
  const engine = useRef(new DeadReckoningEngine());
  const lastSelected = useRef<Aircraft | null>(null);
  const [ready, setReady] = useState(false);
  const [selectedIcao, setSelectedIcao] = useState<string | null>(null);
  const { data: liveTrack } = useActiveTrack(selectedIcao);

  useEffect(() => {
    if (map.current || !mapContainer.current) return;

    mapboxgl.accessToken = process.env.NEXT_PUBLIC_MAPBOX_TOKEN!;

    const instance = new mapboxgl.Map({
      container: mapContainer.current,
      style: "mapbox://styles/mapbox/dark-v11",
      center: [15, 57],
      zoom: 4,
    });

    instance.addControl(new mapboxgl.NavigationControl(), "bottom-right");

    instance.on("load", async () => {
      await loadAircraftIcon(instance);

      instance.addSource("airports", {
        type: "geojson",
        data: airportGeoJSON,
      });

      instance.addSource("aircraft", {
        type: "geojson",
        data: { type: "FeatureCollection", features: [] },
      });

      instance.addSource("track", {
        type: "geojson",
        data: { type: "FeatureCollection", features: [] },
      });

      instance.addSource("live-track", {
        type: "geojson",
        data: { type: "FeatureCollection", features: [] },
      });

      instance.addSource("playback-marker", {
        type: "geojson",
        data: { type: "FeatureCollection", features: [] },
      });

      instance.addLayer({
        id: "track-line",
        type: "line",
        source: "track",
        layout: { "line-join": "round", "line-cap": "round" },
        paint: {
          "line-color": "#63b3ed",
          "line-width": 2,
          "line-opacity": 0.7,
          "line-dasharray": [3, 2],
        },
      });

      instance.addLayer({
        id: "live-track-line",
        type: "line",
        source: "live-track",
        layout: { "line-join": "round", "line-cap": "round" },
        paint: {
          "line-color": "#63b3ed",
          "line-width": 2.5,
          "line-opacity": 0.85,
        },
      });

      instance.addLayer({
        id: "airport-circles",
        type: "circle",
        source: "airports",
        minzoom: 4,
        paint: {
          "circle-radius": 3,
          "circle-color": "#ffffff",
          "circle-opacity": 0.5,
          "circle-stroke-color": "#a0aec0",
          "circle-stroke-width": 1,
          "circle-stroke-opacity": 0.8,
        },
      });

      instance.addLayer({
        id: "airport-labels",
        type: "symbol",
        source: "airports",
        minzoom: 5,
        layout: {
          "text-field": ["get", "iata"],
          "text-size": 10,
          "text-anchor": "top",
          "text-offset": [0, 0.6],
          "text-allow-overlap": false,
        },
        paint: {
          "text-color": "#a0aec0",
          "text-halo-color": "#1a202c",
          "text-halo-width": 1,
        },
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

      instance.addLayer({
        id: "playback-marker",
        type: "circle",
        source: "playback-marker",
        paint: {
          "circle-radius": 8,
          "circle-color": "#f6ad55",
          "circle-stroke-color": "#ffffff",
          "circle-stroke-width": 2,
          "circle-opacity": 0.95,
        },
      });

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
    engine.current.ingest(aircraft, performance.now());
  }, [aircraft]);

  useEffect(() => {
    if (!map.current || !ready) return;
    return startAircraftRenderLoop(
      () =>
        map.current?.getSource("aircraft") as AircraftSource | undefined,
      engine.current,
    );
  }, [ready]);

  useEffect(() => {
    const instance = map.current;
    if (!instance || !ready) return;
    instance.setFilter("aircraft-highlight", [
      "==",
      ["get", "icao24"],
      selectedIcao ?? "",
    ]);
    // A filter change alone may not repaint an idle map, so the selection ring
    // can lag until the next camera move. Force one render to apply it now.
    instance.triggerRepaint();
  }, [selectedIcao, ready]);

  useEffect(() => {
    if (!map.current || !ready) return;
    const source = map.current.getSource("track") as
      | mapboxgl.GeoJSONSource
      | undefined;
    source?.setData(toTrackLineCollection(playbackTrack));
    const points = playbackTrack?.track_points;
    if (points && points.length > 0) {
      const bounds = points.reduce(
        (b, p) => b.extend([p.longitude, p.latitude]),
        new mapboxgl.LngLatBounds(),
      );
      map.current.fitBounds(bounds, {
        padding: { top: 80, bottom: 180, left: 60, right: 60 },
        maxZoom: 9,
        duration: 1200,
      });
    }
  }, [playbackTrack, ready]);

  useEffect(() => {
    if (!map.current || !ready) return;
    const source = map.current.getSource("playback-marker") as
      | mapboxgl.GeoJSONSource
      | undefined;
    source?.setData(toMarkerCollection(playbackPosition));
  }, [playbackPosition, ready]);

  useEffect(() => {
    if (!map.current || !ready) return;
    const getSource = () =>
      map.current?.getSource("live-track") as
        | mapboxgl.GeoJSONSource
        | undefined;

    if (!selectedIcao) {
      getSource()?.setData({ type: "FeatureCollection", features: [] });
      return;
    }

    let frame = 0;
    let last = Number.NEGATIVE_INFINITY;
    const render = () => {
      const now = performance.now();
      if (now - last >= 100) {
        last = now;
        const head = engine.current.position(selectedIcao, now);
        getSource()?.setData(
          toLiveTrailCollection(liveTrack ?? null, selectedIcao, head),
        );
      }
      frame = requestAnimationFrame(render);
    };
    frame = requestAnimationFrame(render);
    return () => cancelAnimationFrame(frame);
  }, [ready, selectedIcao, liveTrack]);

  useEffect(() => {
    if (!selectedIcao) return;
    const id = window.setInterval(() => {
      if (!engine.current.position(selectedIcao, performance.now())) {
        setSelectedIcao(null);
      }
    }, 5000);
    return () => window.clearInterval(id);
  }, [selectedIcao]);

  // The selected plane can be absent from a single broadcast while its icon
  // keeps dead-reckoning, so fall back to the last-known aircraft rather than
  // flickering the panel closed until the next poll.
  const live = selectedIcao
    ? (aircraft.find((a) => a.icao24 === selectedIcao) ?? null)
    : null;
  if (lastSelected.current && lastSelected.current.icao24 !== selectedIcao) {
    lastSelected.current = null;
  }
  if (live) lastSelected.current = live;
  const selected = live ?? lastSelected.current;

  return (
    <div className="relative w-full h-full">
      <div ref={mapContainer} className="w-full h-full" />
      <FlightPanel aircraft={selected} onClose={() => setSelectedIcao(null)} />
    </div>
  );
}
