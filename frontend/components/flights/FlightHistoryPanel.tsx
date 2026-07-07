"use client";

import { useFlights } from "@/hooks/useFlights";
import { FlightSummary } from "@/types/flight";

interface FlightHistoryPanelProps {
  selectedId: string | null;
  onSelect: (id: string) => void;
  onClose: () => void;
}

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
  });
}

function formatDuration(openedAt: string, closedAt: string | null): string {
  if (!closedAt) return "Active";
  const mins = Math.round(
    (new Date(closedAt).getTime() - new Date(openedAt).getTime()) / 60000,
  );
  return `${mins}m`;
}

export default function FlightHistoryPanel({
  selectedId,
  onSelect,
  onClose,
}: FlightHistoryPanelProps) {
  const { data: flights, isLoading, isError } = useFlights();

  return (
    <div className="absolute left-4 right-4 top-16 z-10 sm:right-auto sm:w-72 bg-gray-900 bg-opacity-95 rounded-lg overflow-hidden shadow-xl">
      <div className="flex items-center justify-between px-4 py-3 border-b border-gray-700">
        <h2 className="text-white text-sm font-semibold">Recent Flights</h2>
        <button
          onClick={onClose}
          className="text-gray-400 hover:text-white text-lg leading-none"
        >
          ✕
        </button>
      </div>

      {isLoading && <div className="p-4 text-gray-400 text-sm">Loading...</div>}
      {isError && (
        <div className="p-4 text-red-400 text-sm">Failed to load flights.</div>
      )}

      {!isLoading && !isError && (!flights || flights.length === 0) && (
        <div className="p-4 text-gray-400 text-sm">
          No completed flights yet — check back in a few minutes.
        </div>
      )}

      {flights && flights.length > 0 && (
        <div className="overflow-y-auto max-h-[calc(100dvh-16rem)] sm:max-h-[480px]">
          {flights.map((flight: FlightSummary) => (
            <button
              key={flight.id}
              onClick={() => onSelect(flight.id)}
              className={`w-full text-left px-4 py-3 border-b border-gray-800 hover:bg-gray-800 transition-colors ${
                selectedId === flight.id
                  ? "bg-gray-800 border-l-2 border-l-blue-400"
                  : ""
              }`}
            >
              <div className="flex items-center justify-between">
                <span className="text-white text-sm font-mono">
                  {flight.callsign ?? flight.icao24.toUpperCase()}
                </span>
                <span className="text-gray-500 text-xs">
                  {formatDuration(flight.opened_at, flight.closed_at)}
                </span>
              </div>
              <div className="text-gray-500 text-xs mt-0.5">
                Reg: {flight.origin_country || "Unknown"} ·{" "}
                {formatTime(flight.opened_at)}
                {flight.closed_at ? ` → ${formatTime(flight.closed_at)}` : ""}
              </div>
              {(flight.departure_airport || flight.arrival_airport) && (
                <div className="text-gray-400 text-xs mt-0.5 font-mono">
                  {flight.departure_airport ?? "—"} →{" "}
                  {flight.arrival_airport ?? "—"}
                </div>
              )}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
