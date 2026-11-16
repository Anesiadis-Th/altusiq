"use client";

import { useEvent, useEvents } from "@/hooks/useEvents";
import { AnomalyEventSummary, InvestigationBrief } from "@/types/event";
import { Rail, RailBody, RailHeader } from "@/components/ui/Rail";
import { MicroLabel } from "@/components/ui/Label";
import { focusRingInset } from "@/components/ui/Button";

interface EventsPanelProps {
  selectedId: string | null;
  onSelect: (event: AnomalyEventSummary) => void;
  onClose: () => void;
}

const KIND_LABEL: Record<string, string> = {
  general_emergency: "Emergency",
  radio_failure: "Radio failure",
  unlawful_interference: "Unlawful interference",
};

const INVESTIGATION_LABEL: Record<string, string> = {
  pending: "Brief pending",
  completed: "Brief available",
  invalid_output: "Model output rejected",
  uninvestigated: "Summary from data only",
  skipped: "Not investigated",
};

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
  });
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString([], {
    month: "short",
    day: "numeric",
  });
}

export default function EventsPanel({
  selectedId,
  onSelect,
  onClose,
}: EventsPanelProps) {
  const { data: events, isLoading, isError } = useEvents();

  return (
    <Rail label="Emergency squawk events">
      <RailHeader onClose={onClose} closeLabel="Close events">
        <MicroLabel className="flex-1">Squawk events</MicroLabel>
      </RailHeader>

      {isLoading && (
        <RailBody className="px-3 py-3">
          <p className="text-gray-400 text-[13px]">Loading…</p>
        </RailBody>
      )}

      {isError && (
        <RailBody className="px-3 py-3">
          <p className="text-red-400 text-[13px]">Failed to load events.</p>
        </RailBody>
      )}

      {!isLoading && !isError && (!events || events.length === 0) && (
        <RailBody className="px-3 py-3">
          <p className="text-gray-400 text-[13px] leading-relaxed">
            No emergency squawks confirmed in the last 7 days. A code has to
            hold for two consecutive polls to count.
          </p>
        </RailBody>
      )}

      {events && events.length > 0 && (
        <RailBody>
          {events.map((event) => (
            <div key={event.id} className="border-b border-line last:border-b-0">
              <button
                onClick={() => onSelect(event)}
                className={`w-full px-3 py-1.5 text-left leading-snug transition-colors hover:bg-raised ${focusRingInset} ${
                  selectedId === event.id
                    ? "border-l-2 border-l-amber-400 bg-raised"
                    : ""
                }`}
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="text-white text-[13px] font-mono tabular-nums">
                    {event.callsign?.trim() || event.icao24.toUpperCase()}
                  </span>
                  <span className="text-amber-400 text-xs font-mono tabular-nums">
                    {event.squawk}
                  </span>
                </div>
                <div className="text-gray-500 text-xs">
                  {KIND_LABEL[event.kind] ?? event.kind}
                  <span className="mx-1.5">·</span>
                  <span className="font-mono tabular-nums">
                    {formatDate(event.confirmed_at)} {formatTime(event.confirmed_at)}
                    {event.closed_at ? ` → ${formatTime(event.closed_at)}` : " · open"}
                  </span>
                </div>
                <div className="text-gray-500 text-xs">
                  {INVESTIGATION_LABEL[event.investigation_status] ??
                    event.investigation_status}
                </div>
              </button>
              {selectedId === event.id && <EventBrief id={event.id} />}
            </div>
          ))}
        </RailBody>
      )}
    </Rail>
  );
}

function EventBrief({ id }: { id: string }) {
  const { data, isLoading } = useEvent(id);

  if (isLoading) {
    return (
      <p className="px-3 pb-2 text-gray-500 text-xs">Loading brief…</p>
    );
  }

  const brief = data?.investigation;
  if (!data || !brief) {
    return (
      <p className="px-3 pb-2 text-gray-500 text-xs leading-relaxed">
        No brief for this event yet.
      </p>
    );
  }

  return <BriefBody brief={brief} grounded={data.grounded} model={data.model} />;
}

function BriefBody({
  brief,
  grounded,
  model,
}: {
  brief: InvestigationBrief;
  grounded: boolean;
  model: string | null;
}) {
  return (
    <div className="border-t border-line bg-app/40 px-3 py-2 text-xs leading-relaxed">
      <div className="mb-1.5 flex items-center justify-between gap-2">
        <MicroLabel className="text-amber-400">AI-generated, unverified</MicroLabel>
        <span className="text-gray-600 font-mono tabular-nums">
          {model ?? "no model"}
          {!grounded && " · ungrounded"}
        </span>
      </div>
      <p className="text-gray-200">{brief.summary}</p>

      {brief.explanations.length > 0 && (
        <>
          <MicroLabel className="mt-2 mb-0.5">Possible explanations</MicroLabel>
          <ul className="space-y-1">
            {brief.explanations.map((e, i) => (
              <li key={i} className="text-gray-300">
                <span className="text-gray-500 font-mono">{e.plausibility}</span>{" "}
                {e.hypothesis}
              </li>
            ))}
          </ul>
        </>
      )}

      {brief.unknowns.length > 0 && (
        <>
          <MicroLabel className="mt-2 mb-0.5">Unknown from this data</MicroLabel>
          <ul className="list-disc pl-4 text-gray-400">
            {brief.unknowns.map((u, i) => (
              <li key={i}>{u}</li>
            ))}
          </ul>
        </>
      )}

      <p className="mt-2 text-gray-500">
        Evidence quality: <span className="font-mono">{brief.evidence_quality}</span>
        <span className="mx-1.5">·</span>
        ADS-B shows where an aircraft went, not why. 120 s polls.
      </p>
    </div>
  );
}
