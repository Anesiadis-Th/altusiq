export type InvestigationStatus =
  | "pending"
  | "completed"
  | "invalid_output"
  | "uninvestigated"
  | "skipped";

export interface AnomalyEventSummary {
  id: string;
  icao24: string;
  callsign: string | null;
  origin_country: string | null;
  squawk: string;
  kind: string;
  confirmed_at: string;
  last_seen_at: string;
  closed_at: string | null;
  status: "open" | "closed";
  last_latitude: number | null;
  last_longitude: number | null;
  last_altitude: number | null;
  investigation_status: InvestigationStatus;
  grounded: boolean;
  has_brief: boolean;
}

export interface BriefExplanation {
  hypothesis: string;
  evidence: string[];
  plausibility: "low" | "medium" | "high";
}

// The shape the backend validates before storing. Anything else in
// `investigation` is ignored by the UI.
export interface InvestigationBrief {
  summary: string;
  observations: string[];
  explanations: BriefExplanation[];
  unknowns: string[];
  evidence_quality: "poor" | "fair" | "good";
  recommended_follow_up: string;
}

export interface AnomalyEventDetail extends AnomalyEventSummary {
  first_seen_at: string;
  first_latitude: number | null;
  first_longitude: number | null;
  first_altitude: number | null;
  investigation: InvestigationBrief | null;
  prompt_version: string | null;
  provider: string | null;
  model: string | null;
}
