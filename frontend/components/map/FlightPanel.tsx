"use client";

import { Aircraft } from "@/types/aircraft";
import { RouteAirport } from "@/types/route";
import { useRoute } from "@/hooks/useRoute";
import { haversineKm } from "@/lib/geo";

const MIN_ETA_SPEED_MS = 30;

interface FlightPanelProps {
  aircraft: Aircraft | null;
  onClose: () => void;
}

export default function FlightPanel({ aircraft, onClose }: FlightPanelProps) {
  const { data: route, isLoading: routeLoading } = useRoute(
    aircraft?.callsign ?? null,
  );

  if (!aircraft) return null;

  const altitude = aircraft.barometric_altitude
    ? `${Math.round(aircraft.barometric_altitude).toLocaleString()} m`
    : "Not reporting";

  const speed = aircraft.velocity
    ? `${Math.round(aircraft.velocity * 3.6).toLocaleString()} km/h`
    : "Not reporting";

  const verticalRate = aircraft.vertical_rate
    ? `${aircraft.vertical_rate > 0 ? "↑" : "↓"} ${Math.abs(Math.round(aircraft.vertical_rate))} m/s`
    : "Level";

  const heading = aircraft.heading
    ? `${Math.round(aircraft.heading)}°`
    : "Unknown";

  const destLat = route?.destination.latitude;
  const destLon = route?.destination.longitude;
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
      Date.now() + (km / (aircraft.velocity * 3.6)) * 3_600_000,
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
    <div className="absolute inset-x-0 bottom-0 z-20 max-h-[70dvh] overflow-y-auto rounded-t-2xl border-t border-gray-700 bg-gray-900/95 pb-[env(safe-area-inset-bottom)] shadow-2xl sm:inset-x-auto sm:bottom-auto sm:top-4 sm:right-4 sm:z-10 sm:w-72 sm:max-h-none sm:overflow-hidden sm:rounded-xl sm:border sm:pb-0">
      <div className="mx-auto mt-2 h-1 w-10 rounded-full bg-gray-700 sm:hidden" />
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-700">
        <div>
          <p className="text-white font-semibold text-base tracking-wide">
            {aircraft.callsign?.trim() || "No Callsign"}
          </p>
          <p className="text-gray-400 text-xs mt-0.5">
            {aircraft.icao24.toUpperCase()}
          </p>
        </div>
        <button
          onClick={onClose}
          className="text-gray-500 hover:text-white transition-colors text-xl leading-none"
        >
          ×
        </button>
      </div>

      {/* Route */}
      <div className="px-4 py-3 border-b border-gray-700">
        {routeLoading ? (
          <p className="text-gray-500 text-xs">Looking up route…</p>
        ) : route ? (
          <>
            <div className="flex items-center justify-between gap-2">
              <RouteEndpoint airport={route.origin} />
              <span className="text-gray-500 text-lg shrink-0">→</span>
              <RouteEndpoint airport={route.destination} alignRight />
            </div>
            {route.airline_name && (
              <p className="text-gray-500 text-xs mt-2">
                {route.airline_name}
              </p>
            )}
            {eta && (
              <p
                className="text-gray-400 text-xs mt-2"
                title="Great-circle distance at current ground speed"
              >
                {eta.distance} to go · ETA ≈ {eta.time}
              </p>
            )}
          </>
        ) : (
          <p className="text-gray-500 text-xs">
            Route unknown — no public route data for this callsign
          </p>
        )}
      </div>

      {/* Body */}
      <div className="px-4 py-3 space-y-3">
        <Row label="Registered in" value={aircraft.origin_country ?? "Unknown"} />
        <Row label="Altitude" value={altitude} />
        <Row label="Speed" value={speed} />
        <Row label="Vertical Rate" value={verticalRate} />
        <Row label="Heading" value={heading} />
        <Row
          label="Status"
          value={aircraft.on_ground ? "On Ground" : "Airborne"}
          highlight={!aircraft.on_ground}
        />
      </div>

      {/* Footer */}
      <div className="px-4 py-2 border-t border-gray-700">
        <p className="text-gray-600 text-xs">
          Last contact:{" "}
          {new Date(aircraft.last_contact * 1000).toLocaleTimeString()}
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
      <p className="text-white text-lg font-semibold">
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
}: {
  label: string;
  value: string;
  highlight?: boolean;
}) {
  return (
    <div className="flex justify-between items-center">
      <span className="text-gray-400 text-sm">{label}</span>
      <span
        className={`text-sm font-medium ${
          highlight ? "text-blue-400" : "text-white"
        }`}
      >
        {value}
      </span>
    </div>
  );
}
