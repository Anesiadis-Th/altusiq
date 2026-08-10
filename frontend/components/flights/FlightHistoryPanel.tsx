"use client";

import { useFlights } from "@/hooks/useFlights";
import { FlightSummary } from "@/types/flight";
import { Rail, RailBody, RailHeader } from "@/components/ui/Rail";
import { MicroLabel } from "@/components/ui/Label";
import { focusRingInset } from "@/components/ui/Button";

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
    <Rail label="Recent flights">
      <RailHeader onClose={onClose} closeLabel="Close recent flights">
        <MicroLabel className="flex-1">Recent flights</MicroLabel>
      </RailHeader>

      {isLoading && (
        <RailBody className="px-3 py-3">
          <p className="text-gray-400 text-[13px]">Loading…</p>
        </RailBody>
      )}

      {isError && (
        <RailBody className="px-3 py-3">
          <p className="text-red-400 text-[13px]">Failed to load flights.</p>
        </RailBody>
      )}

      {!isLoading && !isError && (!flights || flights.length === 0) && (
        <RailBody className="px-3 py-3">
          <p className="text-gray-400 text-[13px] leading-relaxed">
            No completed flights yet — check back in a few minutes.
          </p>
        </RailBody>
      )}

      {flights && flights.length > 0 && (
        <RailBody>
          {flights.map((flight: FlightSummary) => (
            <button
              key={flight.id}
              onClick={() => onSelect(flight.id)}
              className={`w-full border-b border-line px-3 py-1.5 text-left leading-snug transition-colors last:border-b-0 hover:bg-raised ${focusRingInset} ${
                selectedId === flight.id
                  ? "border-l-2 border-l-blue-400 bg-raised"
                  : ""
              }`}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="text-white text-[13px] font-mono tabular-nums">
                  {flight.callsign ?? flight.icao24.toUpperCase()}
                </span>
                <span className="text-gray-500 text-xs font-mono tabular-nums">
                  {formatDuration(flight.opened_at, flight.closed_at)}
                </span>
              </div>
              <div className="text-gray-500 text-xs">
                Reg: {flight.origin_country || "Unknown"}
                <span className="mx-1.5">·</span>
                <span className="font-mono tabular-nums">
                  {formatTime(flight.opened_at)}
                  {flight.closed_at ? ` → ${formatTime(flight.closed_at)}` : ""}
                </span>
              </div>
              {(flight.departure_airport || flight.arrival_airport) && (
                <div className="text-gray-400 text-xs font-mono tabular-nums">
                  {flight.departure_airport ?? "—"} →{" "}
                  {flight.arrival_airport ?? "—"}
                </div>
              )}
            </button>
          ))}
        </RailBody>
      )}
    </Rail>
  );
}
