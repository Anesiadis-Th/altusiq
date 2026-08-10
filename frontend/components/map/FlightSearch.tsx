"use client";

import { useMemo, useState } from "react";
import { Search } from "lucide-react";
import { Aircraft } from "@/types/aircraft";
import { searchAircraft } from "@/lib/flightSearch";
import { Rail, RailBody, RailHeader } from "@/components/ui/Rail";
import { MicroLabel } from "@/components/ui/Label";
import { focusRingInset } from "@/components/ui/Button";

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
    <Rail label="Flight search">
      <RailHeader onClose={onClose} closeLabel="Close search">
        <MicroLabel className="flex-1">Search</MicroLabel>
      </RailHeader>

      <div className="shrink-0 border-b border-line p-2">
        <div className="flex h-8 items-center gap-2 rounded-control bg-app px-2.5 focus-within:outline-2 focus-within:-outline-offset-2 focus-within:outline-blue-500">
          <Search size={16} className="shrink-0 text-gray-500" />
          <input
            autoFocus
            value={query}
            onChange={(event) => {
              setQuery(event.target.value);
              setHighlighted(0);
            }}
            onKeyDown={handleKeyDown}
            placeholder="Flight number or callsign…"
            aria-label="Flight number or callsign"
            className="min-w-0 flex-1 bg-transparent text-[13px] text-white placeholder-gray-500 outline-none"
          />
        </div>
      </div>

      {query.trim().length < 2 ? (
        <RailBody className="px-3 py-3">
          <p className="text-gray-500 text-xs leading-relaxed">
            Search live aircraft worldwide. Try a flight number (
            <span className="font-mono tabular-nums">SK4787</span>), a callsign
            (<span className="font-mono tabular-nums">SAS4787</span>) or a hex
            code.
          </p>
        </RailBody>
      ) : results.length === 0 ? (
        <RailBody className="px-3 py-3">
          <p className="text-gray-400 text-[13px]">No matching live flights.</p>
        </RailBody>
      ) : (
        <RailBody>
          {results.map((result, index) => (
            <button
              key={result.aircraft.icao24}
              onClick={() => onSelect(result.aircraft)}
              onMouseEnter={() => setHighlighted(index)}
              className={`flex min-h-11 w-full items-center justify-between gap-2 border-b border-line px-3 py-2 text-left transition-colors last:border-b-0 hover:bg-raised ${focusRingInset} ${
                index === activeIndex ? "bg-raised" : ""
              }`}
            >
              <span className="text-white text-[13px] font-mono tabular-nums">
                {result.label}
              </span>
              <span className="truncate text-gray-500 text-xs">
                {result.aircraft.origin_country ?? (
                  <span className="font-mono tabular-nums">
                    {result.aircraft.icao24.toUpperCase()}
                  </span>
                )}
              </span>
            </button>
          ))}
        </RailBody>
      )}
    </Rail>
  );
}
