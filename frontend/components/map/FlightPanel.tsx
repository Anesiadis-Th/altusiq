"use client";

import { Aircraft } from "@/types/aircraft";

interface FlightPanelProps {
  aircraft: Aircraft | null;
  onClose: () => void;
}

export default function FlightPanel({ aircraft, onClose }: FlightPanelProps) {
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

  return (
    <div className="absolute top-4 right-4 z-10 w-72 bg-gray-900 bg-opacity-95 rounded-xl border border-gray-700 shadow-2xl overflow-hidden">
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

      {/* Body */}
      <div className="px-4 py-3 space-y-3">
        <Row label="Country" value={aircraft.origin_country ?? "Unknown"} />
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
