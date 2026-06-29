"use client";

import { useMemo, useState } from "react";
import Link from "next/link";
import { useFlightData } from "@/hooks/useFlightData";
import { useFlightTrack } from "@/hooks/useFlights";
import { usePlayback } from "@/hooks/usePlayback";
import { Aircraft } from "@/types/aircraft";
import MapView from "./MapView";
import FlightHistoryPanel from "@/components/flights/FlightHistoryPanel";
import PlaybackControls from "@/components/flights/PlaybackControls";

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
  const { aircraft, connected, error } = useFlightData();
  const [showHistory, setShowHistory] = useState(false);
  const [selectedFlightId, setSelectedFlightId] = useState<string | null>(null);

  const { data: track, isLoading: isTrackLoading } =
    useFlightTrack(selectedFlightId);
  const playback = usePlayback(track ?? null);
  const regionalCount = useMemo(
    () => computeRegionalCount(aircraft),
    [aircraft],
  );

  if (error) {
    return (
      <div className="w-full h-screen bg-gray-950 flex items-center justify-center">
        <p className="text-red-400">Connection error: {error}</p>
      </div>
    );
  }

  function handleSelectFlight(id: string) {
    setSelectedFlightId(id);
    setShowHistory(false);
  }

  function handleClosePlayback() {
    playback.pause();
    setSelectedFlightId(null);
  }

  return (
    <div className="relative w-full h-screen">
      <MapView
        aircraft={aircraft}
        connected={connected}
        playbackTrack={track ?? null}
        playbackPosition={playback.currentPosition}
      />

      <TopBar
        connected={connected}
        totalCount={aircraft.length}
        regionalCount={regionalCount}
        showHistory={showHistory}
        onHistoryClick={() => setShowHistory((v) => !v)}
      />

      {showHistory && (
        <FlightHistoryPanel
          selectedId={selectedFlightId}
          onSelect={handleSelectFlight}
          onClose={() => setShowHistory(false)}
        />
      )}

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
  connected: boolean;
  totalCount: number;
  regionalCount: number;
  showHistory: boolean;
  onHistoryClick: () => void;
}

function TopBar({
  connected,
  totalCount,
  regionalCount,
  showHistory,
  onHistoryClick,
}: TopBarProps) {
  return (
    <div className="absolute top-4 left-4 z-10 flex items-center gap-2">
      <div className="bg-gray-900/90 backdrop-blur-sm border border-gray-700/50 rounded-lg px-3 py-2 text-sm flex items-center gap-2">
        <span
          className={`w-2 h-2 rounded-full flex-shrink-0 ${connected ? "bg-green-400" : "bg-red-400"}`}
        />
        {connected ? (
          <span className="text-white">
            {totalCount.toLocaleString()} aircraft
            <span className="text-gray-600 mx-2">·</span>
            <span className="text-blue-400">
              {regionalCount.toLocaleString()} in Scandinavia
            </span>
          </span>
        ) : (
          <span className="text-gray-400">Connecting...</span>
        )}
      </div>

      <button
        onClick={onHistoryClick}
        className={`backdrop-blur-sm border rounded-lg px-3 py-2 text-sm transition-colors ${
          showHistory
            ? "bg-blue-500/20 border-blue-500/50 text-blue-400"
            : "bg-gray-900/90 border-gray-700/50 text-gray-300 hover:text-white hover:border-gray-500/50"
        }`}
      >
        History
      </button>

      <Link
        href="/analytics"
        className="backdrop-blur-sm border rounded-lg px-3 py-2 text-sm transition-colors bg-gray-900/90 border-gray-700/50 text-gray-300 hover:text-white hover:border-gray-500/50"
      >
        Analytics
      </Link>
    </div>
  );
}

function TrackLoadingSkeleton() {
  return (
    <div className="absolute bottom-8 left-1/2 -translate-x-1/2 z-10 w-[520px] bg-gray-900/95 backdrop-blur-sm border border-gray-700/50 rounded-xl px-5 py-4 shadow-xl">
      <div className="flex items-center gap-3 mb-3">
        <div className="w-4 h-4 border-2 border-blue-400 border-t-transparent rounded-full animate-spin flex-shrink-0" />
        <div className="h-3 w-24 bg-gray-700 rounded animate-pulse" />
        <div className="ml-auto h-3 w-16 bg-gray-700 rounded animate-pulse" />
      </div>
      <div className="flex justify-between mb-3 px-1">
        {["ALT", "SPD", "HDG"].map((l) => (
          <div key={l} className="flex flex-col items-center gap-1">
            <div className="h-2 w-6 bg-gray-700 rounded animate-pulse" />
            <div className="h-3 w-16 bg-gray-700 rounded animate-pulse" />
          </div>
        ))}
      </div>
      <div className="h-1 w-full bg-gray-700 rounded animate-pulse mb-3" />
      <div className="flex items-center justify-between">
        <div className="w-8 h-8 rounded-full bg-gray-700 animate-pulse" />
        <div className="flex gap-1">
          {[1, 2, 5, 10, 30].map((s) => (
            <div
              key={s}
              className="w-8 h-6 bg-gray-700 rounded animate-pulse"
            />
          ))}
        </div>
      </div>
    </div>
  );
}
