"use client";

import { PlaybackState } from "@/hooks/usePlayback";
import { FlightTrack } from "@/types/flight";

interface PlaybackControlsProps {
  track: FlightTrack;
  playback: PlaybackState;
  onClose: () => void;
}

function formatTimestamp(unixSeconds: number): string {
  return new Date(unixSeconds * 1000).toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}

function formatAltitude(meters: number | null): string {
  if (meters == null) return "--";
  return `${Math.round(meters * 3.281).toLocaleString()} ft`;
}

function formatSpeed(ms: number | null): string {
  if (ms == null) return "--";
  return `${Math.round(ms * 1.944)} kts`;
}

function formatHeading(deg: number | null): string {
  if (deg == null) return "--";
  return `${Math.round(deg)}°`;
}

const SPEEDS = [1, 2, 5, 10, 30];

export default function PlaybackControls({
  track,
  playback,
  onClose,
}: PlaybackControlsProps) {
  const {
    playing,
    progress,
    speed,
    currentTimestamp,
    currentPosition,
    play,
    pause,
    seek,
    setSpeed,
  } = playback;

  const label = track.callsign ?? track.icao24.toUpperCase();
  const currentTime = currentTimestamp
    ? formatTimestamp(currentTimestamp)
    : "--:--:--";
  const isCompleted = progress >= 1;
  const displayAltitude = isCompleted
    ? null
    : (currentPosition?.altitude ?? null);
  const displayVelocity = isCompleted
    ? null
    : (currentPosition?.velocity ?? null);
  const displayHeading = isCompleted
    ? null
    : (currentPosition?.heading ?? null);

  return (
    <div className="absolute bottom-8 left-1/2 -translate-x-1/2 z-10 w-[520px] bg-gray-900 bg-opacity-95 rounded-xl px-5 py-4 shadow-xl">
      <div className="flex items-center justify-between mb-3">
        <span className="text-white text-sm font-semibold font-mono">
          {label}
        </span>
        <span className="text-gray-400 text-xs">{currentTime}</span>
        <button
          onClick={onClose}
          className="text-gray-400 hover:text-white text-lg leading-none"
        >
          ✕
        </button>
      </div>

      <div className="flex items-center justify-between mb-3 px-1">
        <DataPill label="ALT" value={formatAltitude(displayAltitude)} />
        <DataPill label="SPD" value={formatSpeed(displayVelocity)} />
        <DataPill label="HDG" value={formatHeading(displayHeading)} />
        {isCompleted && (
          <span className="text-xs text-green-400 font-medium">Completed</span>
        )}
      </div>

      <input
        type="range"
        min={0}
        max={1}
        step={0.001}
        value={progress}
        onChange={(e) => seek(parseFloat(e.target.value))}
        className="w-full h-1 accent-blue-400 mb-3 cursor-pointer"
      />

      <div className="flex items-center justify-between">
        <button
          onClick={playing ? pause : play}
          className="w-8 h-8 flex items-center justify-center rounded-full bg-blue-500 hover:bg-blue-400 text-white text-sm transition-colors"
        >
          {playing ? "⏸" : "▶"}
        </button>

        <div className="flex items-center gap-1">
          {SPEEDS.map((s) => (
            <button
              key={s}
              onClick={() => setSpeed(s)}
              className={`px-2 py-1 rounded text-xs transition-colors ${
                speed === s
                  ? "bg-blue-500 text-white"
                  : "bg-gray-700 text-gray-300 hover:bg-gray-600"
              }`}
            >
              {s}×
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}

function DataPill({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col items-center gap-0.5">
      <span className="text-gray-500 text-xs uppercase tracking-wider">
        {label}
      </span>
      <span className="text-white text-sm font-mono tabular-nums">{value}</span>
    </div>
  );
}
