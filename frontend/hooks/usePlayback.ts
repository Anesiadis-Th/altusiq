import { useMemo, useRef, useState, useEffect } from "react";
import { FlightTrack, PlaybackPosition, TrackPoint } from "@/types/flight";

export interface PlaybackState {
  playing: boolean;
  progress: number;
  speed: number;
  duration: number;
  currentPosition: PlaybackPosition | null;
  currentTimestamp: number | null;
  play: () => void;
  pause: () => void;
  seek: (progress: number) => void;
  setSpeed: (speed: number) => void;
}

function interpolatePosition(
  points: TrackPoint[],
  targetTimestamp: number,
): PlaybackPosition | null {
  if (points.length === 0) return null;

  if (targetTimestamp <= points[0].timestamp) {
    const p = points[0];
    return {
      longitude: p.longitude,
      latitude: p.latitude,
      heading: p.heading,
      altitude: p.altitude,
      velocity: p.velocity,
    };
  }
  if (targetTimestamp >= points[points.length - 1].timestamp) {
    const last = points[points.length - 1];
    return {
      longitude: last.longitude,
      latitude: last.latitude,
      heading: last.heading,
      altitude: last.altitude,
      velocity: last.velocity,
    };
  }

  for (let i = 0; i < points.length - 1; i++) {
    const a = points[i];
    const b = points[i + 1];
    if (targetTimestamp >= a.timestamp && targetTimestamp <= b.timestamp) {
      const t = (targetTimestamp - a.timestamp) / (b.timestamp - a.timestamp);
      return {
        longitude: a.longitude + t * (b.longitude - a.longitude),
        latitude: a.latitude + t * (b.latitude - a.latitude),
        heading: b.heading ?? a.heading,
        altitude:
          a.altitude != null && b.altitude != null
            ? a.altitude + t * (b.altitude - a.altitude)
            : (a.altitude ?? b.altitude),
        velocity:
          a.velocity != null && b.velocity != null
            ? a.velocity + t * (b.velocity - a.velocity)
            : (a.velocity ?? b.velocity),
      };
    }
  }

  return null;
}

export function usePlayback(track: FlightTrack | null): PlaybackState {
  const [playing, setPlaying] = useState(false);
  const [progress, setProgress] = useState(0);
  const [speed, setSpeed] = useState(1);
  const rafRef = useRef<number | null>(null);
  const lastTimeRef = useRef<number | null>(null);
  const reachedEndRef = useRef(false);
  const prevTrackIdRef = useRef<string | null>(null);

  // Reset when track changes - during render, not in an effect
  const currentTrackId = track?.id ?? null;
  if (currentTrackId !== prevTrackIdRef.current) {
    prevTrackIdRef.current = currentTrackId;
    setProgress(0);
    setPlaying(false);
  }

  const duration = useMemo(() => {
    if (!track || !track.track_points || track.track_points.length < 2)
      return 0;
    const first = track.track_points[0].timestamp;
    const last = track.track_points[track.track_points.length - 1].timestamp;
    return last - first;
  }, [track]);

  useEffect(() => {
    if (!playing || !track || duration === 0) return;

    reachedEndRef.current = false;

    const animate = (now: number) => {
      if (reachedEndRef.current) {
        setPlaying(false);
        return;
      }

      if (lastTimeRef.current !== null) {
        const delta = (now - lastTimeRef.current) / 1000;
        setProgress((p) => {
          const next = p + (delta * speed) / duration;
          if (next >= 1) {
            reachedEndRef.current = true;
            return 1;
          }
          return next;
        });
      }

      lastTimeRef.current = now;
      rafRef.current = requestAnimationFrame(animate);
    };

    rafRef.current = requestAnimationFrame(animate);

    return () => {
      if (rafRef.current !== null) cancelAnimationFrame(rafRef.current);
      lastTimeRef.current = null;
    };
  }, [playing, track, speed, duration]);

  const currentTimestamp = useMemo(() => {
    if (!track || !track.track_points || track.track_points.length < 2)
      return null;
    return track.track_points[0].timestamp + progress * duration;
  }, [track, progress, duration]);

  const currentPosition = useMemo(() => {
    if (!track || !track.track_points || currentTimestamp === null) return null;
    return interpolatePosition(track.track_points, currentTimestamp);
  }, [track, currentTimestamp]);

  return {
    playing,
    progress,
    speed,
    duration,
    currentPosition,
    currentTimestamp,
    play: () => {
      if (progress >= 1) setProgress(0);
      setPlaying(true);
    },
    pause: () => setPlaying(false),
    seek: (p: number) => setProgress(Math.max(0, Math.min(1, p))),
    setSpeed,
  };
}
