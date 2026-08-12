export interface Aircraft {
  icao24: string;
  callsign: string | null;
  origin_country: string | null;
  longitude: number;
  latitude: number;
  barometric_altitude: number | null;
  on_ground: boolean;
  velocity: number | null;
  heading: number | null;
  vertical_rate: number | null;
  last_contact: number;
}
