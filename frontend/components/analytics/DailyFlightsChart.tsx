"use client";

import { useRef, useState } from "react";
import { useElementWidth } from "@/hooks/useElementWidth";
import { formatCount, formatUtcDay, niceRange } from "@/lib/chart";
import { FlightsPerDay } from "@/types/analytics";
import { ChartTooltip, DataTable, TooltipRow } from "./ChartParts";

const PLOT_H = 168;
const AXIS_H = 20;
const PAD_L = 54;
const PAD_R = 14;
const PAD_T = 14;

export default function DailyFlightsChart({ data }: { data: FlightsPerDay[] }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const width = useElementWidth(containerRef);
  const [hover, setHover] = useState<number | null>(null);

  const plotW = Math.max(0, width - PAD_L - PAD_R);
  const height = PAD_T + PLOT_H + AXIS_H;

  // Cropped axis, not zero-based. Daily volume barely moves, so a zero baseline
  // draws a flat line and hides the weekly rhythm. See ADR-013.
  const counts = data.map((d) => d.count);
  const { min, max, ticks } = niceRange(
    Math.min(...counts),
    Math.max(...counts),
  );

  const x = (i: number) =>
    PAD_L + (data.length < 2 ? plotW / 2 : (i / (data.length - 1)) * plotW);
  const y = (value: number) =>
    PAD_T + PLOT_H - ((value - min) / (max - min)) * PLOT_H;

  const line = data
    .map((d, i) => `${i === 0 ? "M" : "L"}${x(i)},${y(d.count)}`)
    .join(" ");

  const labelEvery = plotW / data.length < 46 ? 4 : 2;
  const point = hover === null ? null : data[hover];

  function handlePointerMove(event: React.PointerEvent<SVGSVGElement>) {
    const bounds = event.currentTarget.getBoundingClientRect();
    const ratio = (event.clientX - bounds.left - PAD_L) / plotW;
    const index = Math.round(ratio * (data.length - 1));
    setHover(Math.max(0, Math.min(data.length - 1, index)));
  }

  return (
    <div ref={containerRef} className="relative">
      {width > 0 && (
        <svg
          width={width}
          height={height}
          role="img"
          aria-label={`Flights per day over the last ${data.length} days`}
          onPointerMove={handlePointerMove}
          onPointerLeave={() => setHover(null)}
        >
          {ticks.map((tick) => (
            <g key={tick}>
              <line
                x1={PAD_L}
                x2={PAD_L + plotW}
                y1={y(tick)}
                y2={y(tick)}
                className="stroke-line"
                strokeWidth={1}
              />
              <text
                x={PAD_L - 10}
                y={y(tick)}
                textAnchor="end"
                dominantBaseline="middle"
                className="fill-gray-500 text-[11px] font-mono tabular-nums"
              >
                {formatCount(tick)}
              </text>
            </g>
          ))}

          <path
            d={line}
            fill="none"
            className="stroke-chart-1"
            strokeWidth={2}
            strokeLinejoin="round"
            strokeLinecap="round"
          />

          {data.map((d, i) =>
            i % labelEvery === 0 ? (
              <text
                key={d.date}
                x={x(i)}
                y={PAD_T + PLOT_H + 14}
                textAnchor="middle"
                className="fill-gray-500 text-[11px] font-mono tabular-nums"
              >
                {formatUtcDay(d.date)}
              </text>
            ) : null,
          )}

          {point && (
            <g>
              <line
                x1={x(hover!)}
                x2={x(hover!)}
                y1={PAD_T}
                y2={PAD_T + PLOT_H}
                className="stroke-gray-600"
                strokeWidth={1}
              />
              <circle
                cx={x(hover!)}
                cy={y(point.count)}
                r={4}
                className="fill-chart-1 stroke-surface"
                strokeWidth={2}
              />
            </g>
          )}
        </svg>
      )}

      {point && (
        <ChartTooltip
          x={Math.min(Math.max(x(hover!), 60), width - 60)}
          y={y(point.count)}
        >
          <TooltipRow
            keyClass="bg-chart-1"
            value={formatCount(point.count)}
            label={formatUtcDay(point.date, "long")}
          />
        </ChartTooltip>
      )}

      <DataTable
        caption="Flights per day"
        columns={["Day (UTC)", "Flights"]}
        rows={data.map((d) => [d.date, formatCount(d.count)])}
      />
    </div>
  );
}
