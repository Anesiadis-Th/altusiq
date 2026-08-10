"use client";

import { useMemo, useRef, useState } from "react";
import Link from "next/link";
import { BarChart3, History, Plane, Search } from "lucide-react";
import { useFlightData } from "@/hooks/useFlightData";
import { useFlightTrack } from "@/hooks/useFlights";
import { usePlayback } from "@/hooks/usePlayback";
import { useActivePanel } from "@/hooks/useActivePanel";
import { Aircraft } from "@/types/aircraft";
import MapView, { FocusTarget } from "./MapView";
import FlightSearch from "./FlightSearch";
import FlightHistoryPanel from "@/components/flights/FlightHistoryPanel";
import PlaybackControls from "@/components/flights/PlaybackControls";
import { MicroLabel } from "@/components/ui/Label";
import {
  ControlGroup,
  GroupButton,
  GroupDivider,
  groupItemClasses,
} from "@/components/ui/Button";

const SCANDINAVIA_BBOX = {
  minLon: 4.0,
  maxLon: 32.0,
  minLat: 54.0,
  maxLat: 72.0,
};

function computeRegionalCount(aircraft: Aircraft[]): number {
  return aircraft.filter(
    (a) =>
      a.longitude != null &&
      a.latitude != null &&
      a.longitude >= SCANDINAVIA_BBOX.minLon &&
      a.longitude <= SCANDINAVIA_BBOX.maxLon &&
      a.latitude >= SCANDINAVIA_BBOX.minLat &&
      a.latitude <= SCANDINAVIA_BBOX.maxLat,
  ).length;
}

export default function FlightMap() {
  const { aircraft, connected } = useFlightData();
  const [selectedFlightId, setSelectedFlightId] = useState<string | null>(null);
  const [selectedIcao, setSelectedIcao] = useState<string | null>(null);
  const [focusTarget, setFocusTarget] = useState<FocusTarget | null>(null);
  const focusSeq = useRef(0);
  const panelRef = useRef<HTMLDivElement>(null);
  const topBarRef = useRef<HTMLDivElement>(null);
  const {
    activePanel,
    toggle: togglePanel,
    close: closePanel,
  } = useActivePanel({ panelRef, topBarRef });

  const { data: track, isLoading: isTrackLoading } =
    useFlightTrack(selectedFlightId);
  const playback = usePlayback(track ?? null);
  const regionalCount = useMemo(
    () => computeRegionalCount(aircraft),
    [aircraft],
  );

  function handleSelectIcao(icao24: string | null) {
    setSelectedIcao(icao24);
    if (!icao24) return;
    playback.pause();
    setSelectedFlightId(null);
  }

  function handleSelectFlight(id: string) {
    setSelectedFlightId(id);
    setSelectedIcao(null);
    closePanel();
  }

  function handleSearchSelect(plane: Aircraft) {
    focusSeq.current += 1;
    handleSelectIcao(plane.icao24);
    setFocusTarget({
      icao24: plane.icao24,
      longitude: plane.longitude,
      latitude: plane.latitude,
      seq: focusSeq.current,
    });
    closePanel();
  }

  function handleClosePlayback() {
    playback.pause();
    setSelectedFlightId(null);
  }

  return (
    <div className="relative w-full h-dvh">
      <MapView
        aircraft={aircraft}
        selectedIcao={selectedIcao}
        onSelectIcao={handleSelectIcao}
        focusTarget={focusTarget}
        playbackTrack={track ?? null}
        playbackPosition={playback.currentPosition}
      />

      <TopBar
        ref={topBarRef}
        connected={connected}
        totalCount={aircraft.length}
        regionalCount={regionalCount}
        showSearch={activePanel === "search"}
        onSearchClick={() => togglePanel("search")}
        showHistory={activePanel === "history"}
        onHistoryClick={() => togglePanel("history")}
      />

      <div ref={panelRef}>
        {activePanel === "search" && (
          <FlightSearch
            aircraft={aircraft}
            onSelect={handleSearchSelect}
            onClose={() => closePanel()}
          />
        )}

        {activePanel === "history" && (
          <FlightHistoryPanel
            selectedId={selectedFlightId}
            onSelect={handleSelectFlight}
            onClose={() => closePanel()}
          />
        )}
      </div>

      {selectedFlightId && isTrackLoading && <TrackLoadingSkeleton />}

      {track && !isTrackLoading && (
        <PlaybackControls
          track={track}
          playback={playback}
          onClose={handleClosePlayback}
        />
      )}
    </div>
  );
}

interface TopBarProps {
  ref?: React.Ref<HTMLDivElement>;
  connected: boolean;
  totalCount: number;
  regionalCount: number;
  showSearch: boolean;
  onSearchClick: () => void;
  showHistory: boolean;
  onHistoryClick: () => void;
}

function TopBar({
  ref,
  connected,
  totalCount,
  regionalCount,
  showSearch,
  onSearchClick,
  showHistory,
  onHistoryClick,
}: TopBarProps) {
  // The rail is docked flush to the left edge, so the toolbar steps aside for
  // it on sm+ rather than sitting on top of it.
  const railOpen = showSearch || showHistory;

  return (
    <div
      ref={ref}
      className={`absolute top-4 left-4 right-4 z-20 flex items-center gap-2 transition-[left] duration-200 sm:right-auto ${
        railOpen ? "sm:left-84" : "sm:left-4"
      }`}
    >
      <div className="flex h-8 min-w-0 items-center gap-2 rounded-control border border-line bg-surface px-2.5">
        <span
          className={`h-2 w-2 shrink-0 rounded-full ${connected ? "bg-green-400" : "bg-red-400"}`}
        />
        {connected ? (
          <span
            className="flex min-w-0 items-center gap-1.5 whitespace-nowrap text-[13px]"
            title={`${regionalCount.toLocaleString()} in Scandinavia`}
          >
            <span className="font-mono tabular-nums text-white">
              {totalCount.toLocaleString()}
            </span>
            <span className="hidden text-gray-400 sm:inline">aircraft</span>
            <Plane size={14} className="shrink-0 text-gray-400 sm:hidden" />
            <span className="text-gray-600">·</span>
            <span className="font-mono tabular-nums text-blue-400">
              {regionalCount.toLocaleString()}
            </span>
            <span className="hidden text-gray-400 sm:inline">
              in Scandinavia
            </span>
          </span>
        ) : (
          <span className="text-gray-400 text-[13px] whitespace-nowrap">
            Connecting…
          </span>
        )}
      </div>

      <ControlGroup>
        <GroupButton
          active={showSearch}
          onClick={onSearchClick}
          aria-label="Search flights"
        >
          <Search size={16} />
          <span className="hidden sm:inline">Search</span>
        </GroupButton>
        <GroupDivider />
        <GroupButton
          active={showHistory}
          onClick={onHistoryClick}
          aria-label="Recent flights"
        >
          <History size={16} />
          <span className="hidden sm:inline">History</span>
        </GroupButton>
        <GroupDivider />
        <Link
          href="/analytics"
          aria-label="Analytics"
          className={groupItemClasses()}
        >
          <BarChart3 size={16} />
          <span className="hidden sm:inline">Analytics</span>
        </Link>
      </ControlGroup>
    </div>
  );
}

function TrackLoadingSkeleton() {
  return (
    <div className="absolute bottom-4 sm:bottom-8 left-1/2 -translate-x-1/2 z-10 w-[calc(100%-2rem)] max-w-[520px] rounded-panel border border-line bg-surface px-4 py-4 sm:px-5">
      <div className="flex items-center gap-3 mb-3">
        <div className="w-4 h-4 border-2 border-blue-400 border-t-transparent rounded-full animate-spin flex-shrink-0" />
        <div className="h-3 w-24 bg-raised rounded-control animate-pulse" />
        <div className="ml-auto h-3 w-16 bg-raised rounded-control animate-pulse" />
      </div>
      <div className="flex justify-between mb-3 px-1">
        {["ALT", "SPD", "HDG"].map((l) => (
          <div key={l} className="flex flex-col items-center gap-1">
            <MicroLabel className="text-gray-600">{l}</MicroLabel>
            <div className="h-3 w-16 bg-raised rounded-control animate-pulse" />
          </div>
        ))}
      </div>
      <div className="h-1 w-full bg-raised rounded-control animate-pulse mb-3" />
      <div className="flex items-center justify-between">
        <div className="w-8 h-8 rounded-full bg-raised animate-pulse" />
        <div className="flex gap-1">
          {[1, 2, 5, 10, 30].map((s) => (
            <div
              key={s}
              className="w-8 h-6 bg-raised rounded-control animate-pulse"
            />
          ))}
        </div>
      </div>
    </div>
  );
}
