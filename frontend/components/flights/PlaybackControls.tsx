"use client";

import { Fragment } from "react";
import { Pause, Play, X } from "lucide-react";
import { PlaybackState } from "@/hooks/usePlayback";
import { FlightTrack } from "@/types/flight";
import { MicroLabel } from "@/components/ui/Label";
import {
  ControlGroup,
  GroupButton,
  GroupDivider,
  IconButton,
  focusRing,
} from "@/components/ui/Button";

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

const SPEEDS = [0.5, 1, 2, 4];

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
  const displayAltitude = currentPosition?.altitude ?? null;
  const displayVelocity = currentPosition?.velocity ?? null;
  const displayHeading = currentPosition?.heading ?? null;

  return (
    <div className="absolute bottom-4 sm:bottom-8 left-1/2 -translate-x-1/2 z-30 w-[calc(100%-2rem)] max-w-[520px] rounded-panel border border-line bg-surface px-4 py-4 sm:px-5">
      <div className="mb-3 flex items-center gap-3">
        <span className="text-white text-[13px] font-semibold font-mono tabular-nums">
          {label}
        </span>
        <span className="text-gray-400 text-xs font-mono tabular-nums">
          {currentTime}
        </span>
        {isCompleted && (
          <span className="text-green-400 text-xs font-medium">Completed</span>
        )}
        <IconButton
          onClick={onClose}
          aria-label="Close playback"
          className="ml-auto"
        >
          <X size={16} />
        </IconButton>
      </div>

      <div className="mb-3 flex items-center justify-between px-1">
        <DataPill label="ALT" value={formatAltitude(displayAltitude)} />
        <DataPill label="SPD" value={formatSpeed(displayVelocity)} />
        <DataPill label="HDG" value={formatHeading(displayHeading)} />
      </div>

      <input
        type="range"
        min={0}
        max={1}
        step={0.001}
        value={progress}
        aria-label="Playback position"
        onPointerDown={() => pause()}
        onChange={(e) => seek(parseFloat(e.target.value))}
        className={`mb-3 h-1 w-full cursor-pointer accent-blue-400 ${focusRing}`}
      />

      <div className="flex items-center justify-between">
        <button
          onClick={playing ? pause : play}
          aria-label={playing ? "Pause playback" : "Play playback"}
          className={`flex h-8 w-8 items-center justify-center rounded-full bg-blue-500 text-white transition-colors hover:bg-blue-400 active:bg-blue-500 ${focusRing}`}
        >
          {playing ? <Pause size={16} /> : <Play size={16} />}
        </button>

        <ControlGroup>
          {SPEEDS.map((s, i) => (
            // Fragment, not a wrapper element: the group's first:/last: corner
            // rounding needs the buttons to stay direct children.
            <Fragment key={s}>
              {i > 0 && <GroupDivider />}
              <GroupButton
                active={speed === s}
                onClick={() => setSpeed(s)}
                aria-label={`${s}× speed`}
                className="font-mono tabular-nums"
              >
                {s}×
              </GroupButton>
            </Fragment>
          ))}
        </ControlGroup>
      </div>
    </div>
  );
}

function DataPill({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex flex-col items-center gap-0.5">
      <MicroLabel>{label}</MicroLabel>
      <span className="text-white text-[13px] font-mono tabular-nums">
        {value}
      </span>
    </div>
  );
}
