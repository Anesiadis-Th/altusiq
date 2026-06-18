export interface FlightSummary {
  id: string;
  icao24: string;
  callsign: string | null;
  origin_country: string | null;
  opened_at: string;
  closed_at: string | null;
  last_latitude: number | null;
  last_longitude: number | null;
  departure_airport: string | null;
  arrival_airport: string | null;
}

export interface TrackPoint {
  timestamp: number;
  longitude: number;
  latitude: number;
  altitude: number | null;
  heading: number | null;
  velocity: number | null;
}

export interface FlightTrack {
  id: string;
  icao24: string;
  callsign: string | null;
  origin_country: string | null;
  opened_at: string;
  closed_at: string | null;
  track_points: TrackPoint[];
}

export interface PlaybackPosition {
  longitude: number;
  latitude: number;
  heading: number | null;
  altitude: number | null;
  velocity: number | null;
}
