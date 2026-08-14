"use client";

import { useRef, useState } from "react";
import { useElementWidth } from "@/hooks/useElementWidth";
import { formatCount } from "@/lib/chart";
import { ChartTooltip, DataTable, TooltipRow } from "./ChartParts";

export interface RankedSegment {
  keyClass: string;
  label: string;
  value: number;
}

export interface RankedRow {
  id: string;
  label: string;
  caption?: string | null;
  total: number;
  segments?: RankedSegment[];
}

interface HoverState {
  row: RankedRow;
  x: number;
  y: number;
}

export default function RankedBars({
  rows,
  tableCaption,
}: {
  rows: RankedRow[];
  tableCaption: string;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const width = useElementWidth(containerRef);
  const [hover, setHover] = useState<HoverState | null>(null);

  const max = Math.max(...rows.map((r) => r.total), 1);
  const hasSegments = rows.some((r) => r.segments && r.segments.length > 1);

  function handlePointerMove(row: RankedRow, event: React.PointerEvent) {
    if (!row.segments || row.segments.length < 2) return;
    const bounds = containerRef.current?.getBoundingClientRect();
    if (!bounds) return;
    setHover({
      row,
      x: event.clientX - bounds.left,
      y: event.clientY - bounds.top,
    });
  }

  return (
    <div ref={containerRef} className="relative">
      <ul className="flex flex-col gap-2.5">
        {rows.map((row) => {
          const segments = (row.segments ?? [
            { keyClass: "bg-chart-1", label: "Flights", value: row.total },
          ]).filter((segment) => segment.value > 0);

          return (
            <li
              key={row.id}
              className="-mx-2 rounded-control px-2 py-1 transition-colors hover:bg-raised/60"
              onPointerMove={(event) => handlePointerMove(row, event)}
              onPointerLeave={() => setHover(null)}
            >
              <div className="flex items-baseline gap-2">
                <span className="shrink-0 font-mono text-[13px] text-white">
                  {row.label}
                </span>
                {row.caption && (
                  <span className="min-w-0 flex-1 truncate text-gray-500 text-xs">
                    {row.caption}
                  </span>
                )}
                <span className="ml-auto shrink-0 pl-2 font-mono text-[13px] tabular-nums text-gray-300">
                  {formatCount(row.total)}
                </span>
              </div>

              <div
                className="mt-1.5 flex h-2 gap-[2px]"
                style={{ width: `${(row.total / max) * 100}%` }}
              >
                {segments.map((segment, i) => (
                  <div
                    key={segment.label}
                    className={`h-full ${segment.keyClass} ${
                      i === segments.length - 1 ? "rounded-r-[4px]" : ""
                    }`}
                    style={{ flexGrow: segment.value, flexBasis: 0 }}
                  />
                ))}
              </div>
            </li>
          );
        })}
      </ul>

      {hover?.row.segments && (
        <ChartTooltip
          x={Math.min(Math.max(hover.x, 80), width - 80)}
          y={hover.y}
        >
          <span className="flex flex-col gap-0.5">
            {hover.row.segments.map((segment) => (
              <TooltipRow
                key={segment.label}
                keyClass={segment.keyClass}
                value={formatCount(segment.value)}
                label={segment.label}
              />
            ))}
          </span>
        </ChartTooltip>
      )}

      {hasSegments && (
        <DataTable
          caption={tableCaption}
          columns={[
            "Name",
            ...(rows[0]?.segments ?? []).map((segment) => segment.label),
            "Total",
          ]}
          rows={rows.map((row) => [
            row.caption ? `${row.label} (${row.caption})` : row.label,
            ...(row.segments ?? []).map((segment) => formatCount(segment.value)),
            formatCount(row.total),
          ])}
        />
      )}
    </div>
  );
}
