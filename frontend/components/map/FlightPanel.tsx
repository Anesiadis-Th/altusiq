"use client";

import { useRef, useState } from "react";
import { X } from "lucide-react";
import { Aircraft } from "@/types/aircraft";
import { RouteAirport } from "@/types/route";
import { useRoute } from "@/hooks/useRoute";
import { haversineKm, isRoutePlausible } from "@/lib/geo";
import { MicroLabel } from "@/components/ui/Label";
import { IconButton } from "@/components/ui/Button";

const MIN_ETA_SPEED_MS = 30;
const DISMISS_DISTANCE_PX = 120;
const DISMISS_VELOCITY_PX_MS = 0.5;
const TAP_MAX_DISTANCE_PX = 10;
const TAP_MAX_MS = 300;
const DISMISS_ANIMATION_MS = 200;

interface DragSample {
  startY: number;
  startTime: number;
  lastY: number;
  lastTime: number;
  prevY: number;
  prevTime: number;
}

interface FlightPanelProps {
  aircraft: Aircraft | null;
  onClose: () => void;
}

export default function FlightPanel({ aircraft, onClose }: FlightPanelProps) {
  const { data: route, isLoading: routeLoading } = useRoute(
    aircraft?.callsign ?? null,
  );

  const sheetRef = useRef<HTMLDivElement>(null);
  const dragRef = useRef<DragSample | null>(null);
  const [dragY, setDragY] = useState(0);
  const [isDragging, setIsDragging] = useState(false);

  const dismiss = () => {
    setIsDragging(false);
    setDragY(sheetRef.current?.offsetHeight ?? 400);
    window.setTimeout(() => {
      onClose();
      setDragY(0);
    }, DISMISS_ANIMATION_MS);
  };

  const onHandlePointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    e.currentTarget.setPointerCapture(e.pointerId);
    const now = performance.now();
    dragRef.current = {
      startY: e.clientY,
      startTime: now,
      lastY: e.clientY,
      lastTime: now,
      prevY: e.clientY,
      prevTime: now,
    };
    setIsDragging(true);
  };

  const onHandlePointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    const drag = dragRef.current;
    if (!drag) return;
    drag.prevY = drag.lastY;
    drag.prevTime = drag.lastTime;
    drag.lastY = e.clientY;
    drag.lastTime = performance.now();
    setDragY(Math.max(0, e.clientY - drag.startY));
  };

  const onHandlePointerUp = () => {
    const drag = dragRef.current;
    if (!drag) return;
    dragRef.current = null;
    const dragged = Math.max(0, drag.lastY - drag.startY);
    const dt = drag.lastTime - drag.prevTime;
    const velocity = dt > 0 ? (drag.lastY - drag.prevY) / dt : 0;
    const sheetHeight = sheetRef.current?.offsetHeight ?? 0;
    const isTap =
      dragged < TAP_MAX_DISTANCE_PX &&
      drag.lastTime - drag.startTime < TAP_MAX_MS;
    const threshold = Math.min(
      DISMISS_DISTANCE_PX,
      sheetHeight > 0 ? sheetHeight / 3 : DISMISS_DISTANCE_PX,
    );
    if (isTap || dragged > threshold || velocity > DISMISS_VELOCITY_PX_MS) {
      dismiss();
    } else {
      setIsDragging(false);
      setDragY(0);
    }
  };

  const onHandlePointerCancel = () => {
    dragRef.current = null;
    setIsDragging(false);
    setDragY(0);
  };

  if (!aircraft) return null;

  const altitude =
    aircraft.barometric_altitude != null
      ? `${Math.round(aircraft.barometric_altitude).toLocaleString()} m`
      : "Not reporting";

  const speed =
    aircraft.velocity != null
      ? `${Math.round(aircraft.velocity * 3.6).toLocaleString()} km/h`
      : "Not reporting";

  const verticalRate = aircraft.vertical_rate
    ? `${aircraft.vertical_rate > 0 ? "↑" : "↓"} ${Math.abs(Math.round(aircraft.vertical_rate))} m/s`
    : "Level";

  const heading =
    aircraft.heading != null ? `${Math.round(aircraft.heading)}°` : "Unknown";

  const plausibleRoute =
    route && isRoutePlausible(route, aircraft.latitude, aircraft.longitude)
      ? route
      : null;

  const destLat = plausibleRoute?.destination.latitude;
  const destLon = plausibleRoute?.destination.longitude;
  let eta: { distance: string; time: string } | null = null;
  if (
    destLat != null &&
    destLon != null &&
    !aircraft.on_ground &&
    aircraft.velocity != null &&
    aircraft.velocity > MIN_ETA_SPEED_MS
  ) {
    const km = haversineKm(
      aircraft.latitude,
      aircraft.longitude,
      destLat,
      destLon,
    );
    const etaDate = new Date(
      aircraft.last_contact * 1000 +
        (km / (aircraft.velocity * 3.6)) * 3_600_000,
    );
    eta = {
      distance: `${Math.round(km).toLocaleString()} km`,
      time: etaDate.toLocaleTimeString([], {
        hour: "2-digit",
        minute: "2-digit",
      }),
    };
  }

  return (
    <div
      ref={sheetRef}
      className="absolute inset-x-0 bottom-0 z-20 max-h-[70dvh] overflow-y-auto rounded-t-panel border-t border-line bg-surface pb-[env(safe-area-inset-bottom)] sm:inset-x-auto sm:bottom-auto sm:top-4 sm:right-4 sm:z-10 sm:w-80 sm:max-h-none sm:overflow-hidden sm:rounded-panel sm:border sm:border-line sm:pb-0"
      style={{
        transform: dragY > 0 ? `translateY(${dragY}px)` : undefined,
        transition: isDragging
          ? "none"
          : `transform ${DISMISS_ANIMATION_MS}ms ease-out`,
      }}
    >
      <div
        role="button"
        aria-label="Close flight panel"
        className="touch-none select-none py-3 sm:hidden"
        onPointerDown={onHandlePointerDown}
        onPointerMove={onHandlePointerMove}
        onPointerUp={onHandlePointerUp}
        onPointerCancel={onHandlePointerCancel}
      >
        <div className="mx-auto h-1 w-10 rounded-full bg-gray-700" />
      </div>
      <div className="flex items-center justify-between gap-2 px-4 py-3 border-b border-line">
        <div className="min-w-0">
          <p className="text-white font-semibold text-base font-mono tabular-nums">
            {aircraft.callsign?.trim() || "No Callsign"}
          </p>
          <p className="text-gray-400 text-xs font-mono tabular-nums mt-0.5">
            {aircraft.icao24.toUpperCase()}
          </p>
        </div>
        <IconButton onClick={onClose} aria-label="Close flight panel">
          <X size={16} />
        </IconButton>
      </div>

      <div className="px-4 py-3 border-b border-line">
        {routeLoading ? (
          <p className="text-gray-500 text-xs">Looking up route…</p>
        ) : plausibleRoute ? (
          <>
            <div className="flex items-center justify-between gap-2">
              <RouteEndpoint airport={plausibleRoute.origin} />
              <span className="text-gray-500 text-lg shrink-0">→</span>
              <RouteEndpoint airport={plausibleRoute.destination} alignRight />
            </div>
            {plausibleRoute.airline_name && (
              <p className="text-gray-500 text-xs mt-2">
                {plausibleRoute.airline_name}
              </p>
            )}
            {eta && (
              <p
                className="text-gray-400 text-xs mt-2"
                title="Great-circle distance at current ground speed"
              >
                <span className="font-mono tabular-nums">{eta.distance}</span>{" "}
                to go · ETA ≈{" "}
                <span className="font-mono tabular-nums">{eta.time}</span>
              </p>
            )}
          </>
        ) : (
          <p className="text-gray-500 text-xs">
            {route
              ? "Route unknown. The published route for this callsign doesn't match this aircraft's position"
              : "Route unknown. No public route data for this callsign"}
          </p>
        )}
      </div>

      <div className="px-4 py-3">
        <Row
          label="Registered in"
          value={aircraft.origin_country ?? "Unknown"}
          mono={false}
        />
        <Row label="Altitude" value={altitude} />
        <Row label="Speed" value={speed} />
        <Row label="Vertical Rate" value={verticalRate} />
        <Row label="Heading" value={heading} />
        <Row
          label="Status"
          value={aircraft.on_ground ? "On Ground" : "Airborne"}
          highlight={!aircraft.on_ground}
          mono={false}
        />
      </div>

      <div className="px-4 py-2 border-t border-line">
        <p className="text-gray-600 text-xs">
          Last contact:{" "}
          <span className="font-mono tabular-nums">
            {new Date(aircraft.last_contact * 1000).toLocaleTimeString()}
          </span>
        </p>
      </div>
    </div>
  );
}

function RouteEndpoint({
  airport,
  alignRight = false,
}: {
  airport: RouteAirport;
  alignRight?: boolean;
}) {
  return (
    <div className={`min-w-0 ${alignRight ? "text-right" : ""}`}>
      <p className="text-white text-lg font-semibold font-mono tabular-nums">
        {airport.iata_code ?? airport.icao_code ?? "?"}
      </p>
      <p className="text-gray-400 text-xs truncate">
        {airport.municipality ?? airport.name ?? ""}
      </p>
    </div>
  );
}

function Row({
  label,
  value,
  highlight = false,
  mono = true,
}: {
  label: string;
  value: string;
  highlight?: boolean;
  mono?: boolean;
}) {
  return (
    <div className="flex h-10 items-center justify-between gap-3">
      <MicroLabel>{label}</MicroLabel>
      <span
        className={`text-[13px] font-medium ${mono ? "font-mono tabular-nums" : ""} ${
          highlight ? "text-blue-400" : "text-white"
        }`}
      >
        {value}
      </span>
    </div>
  );
}
