"use client";

import { useMemo, useState } from "react";
import { Aircraft } from "@/types/aircraft";
import { searchAircraft } from "@/lib/flightSearch";

interface FlightSearchProps {
  aircraft: Aircraft[];
  onSelect: (aircraft: Aircraft) => void;
  onClose: () => void;
}

export default function FlightSearch({
  aircraft,
  onSelect,
  onClose,
}: FlightSearchProps) {
  const [query, setQuery] = useState("");
  const [highlighted, setHighlighted] = useState(0);

  const results = useMemo(
    () => searchAircraft(aircraft, query),
    [aircraft, query],
  );
  const activeIndex = Math.min(highlighted, Math.max(results.length - 1, 0));

  function handleKeyDown(event: React.KeyboardEvent<HTMLInputElement>) {
    if (event.key === "Escape") {
      onClose();
    } else if (event.key === "ArrowDown") {
      event.preventDefault();
      setHighlighted((i) => Math.min(i + 1, results.length - 1));
    } else if (event.key === "ArrowUp") {
      event.preventDefault();
      setHighlighted((i) => Math.max(i - 1, 0));
    } else if (event.key === "Enter" && results[activeIndex]) {
      onSelect(results[activeIndex].aircraft);
    }
  }

  return (
    <div className="absolute left-4 right-4 top-16 z-10 sm:right-auto sm:w-80 bg-gray-900 bg-opacity-95 rounded-lg overflow-hidden shadow-xl">
      <div className="flex items-center gap-2 px-4 py-3 border-b border-gray-700">
        <span className="text-gray-500 text-sm">🔍</span>
        <input
          autoFocus
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            setHighlighted(0);
          }}
          onKeyDown={handleKeyDown}
          placeholder="Flight number or callsign…"
          className="flex-1 min-w-0 bg-transparent text-white text-sm placeholder-gray-500 outline-none"
        />
        <button
          onClick={onClose}
          className="text-gray-400 hover:text-white text-lg leading-none"
        >
          ✕
        </button>
      </div>

      {query.trim().length < 2 ? (
        <div className="p-4 text-gray-500 text-xs">
          Search live aircraft worldwide — try a flight number (SK4787), a
          callsign (SAS4787) or a hex code.
        </div>
      ) : results.length === 0 ? (
        <div className="p-4 text-gray-400 text-sm">
          No matching live flights.
        </div>
      ) : (
        <div className="overflow-y-auto max-h-[calc(100dvh-16rem)] sm:max-h-[360px]">
          {results.map((result, index) => (
            <button
              key={result.aircraft.icao24}
              onClick={() => onSelect(result.aircraft)}
              onMouseEnter={() => setHighlighted(index)}
              className={`w-full text-left px-4 py-2.5 border-b border-gray-800 transition-colors ${
                index === activeIndex ? "bg-gray-800" : ""
              }`}
            >
              <div className="flex items-center justify-between gap-2">
                <span className="text-white text-sm font-mono">
                  {result.label}
                </span>
                <span className="text-gray-500 text-xs truncate">
                  {result.aircraft.origin_country ??
                    result.aircraft.icao24.toUpperCase()}
                </span>
              </div>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
