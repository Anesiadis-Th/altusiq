"use client";

import { useRef, useState } from "react";
import { useElementWidth } from "@/hooks/useElementWidth";
import { formatCount, niceAxis, roundedTopBar } from "@/lib/chart";
import { FlightsPerHour } from "@/types/analytics";
import { ChartTooltip, DataTable, TooltipRow } from "./ChartParts";

const PLOT_H = 168;
const AXIS_H = 20;
const PAD_L = 46;
const PAD_R = 14;
const PAD_T = 20; // room for the peak column's cap label
const MAX_COL_W = 24;
const GAP = 2;
const LABELLED_HOURS = [0, 6, 12, 18];

const formatHour = (hour: number) => `${String(hour).padStart(2, "0")}:00`;

export default function HourlyFlightsChart({
  data,
}: {
  data: FlightsPerHour[];
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const width = useElementWidth(containerRef);
  const [hover, setHover] = useState<number | null>(null);

  const plotW = Math.max(0, width - PAD_L - PAD_R);
  const height = PAD_T + PLOT_H + AXIS_H;
  const { max, ticks } = niceAxis(Math.max(...data.map((d) => d.count), 0));

  const band = plotW / data.length;
  const colW = Math.min(MAX_COL_W, Math.max(1, band - GAP));
  const bandX = (i: number) => PAD_L + i * band;
  const colX = (i: number) => bandX(i) + (band - colW) / 2;
  const y = (value: number) => PAD_T + PLOT_H - (value / max) * PLOT_H;

  const peak = data.reduce((best, d) => (d.count > best.count ? d : best), data[0]);
  const point = hover === null ? null : data[hover];

  return (
    <div ref={containerRef} className="relative">
      {width > 0 && (
        <svg
          width={width}
          height={height}
          role="img"
          aria-label="Flights by hour of day, UTC"
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
                className="fill-gray-500 text-[11px] tabular-nums"
              >
                {formatCount(tick)}
              </text>
            </g>
          ))}

          {data.map((d, i) => (
            <path
              key={d.hour}
              d={roundedTopBar(
                colX(i),
                y(d.count),
                colW,
                PAD_T + PLOT_H - y(d.count),
              )}
              className={
                hover === i ? "fill-chart-1 brightness-125" : "fill-chart-1"
              }
            />
          ))}

          <text
            x={colX(data.indexOf(peak)) + colW / 2}
            y={y(peak.count) - 7}
            textAnchor="middle"
            className="fill-gray-300 text-[11px] font-medium tabular-nums"
          >
            {formatCount(peak.count)}
          </text>

          {data.map((d, i) =>
            LABELLED_HOURS.includes(d.hour) ? (
              <text
                key={d.hour}
                x={colX(i) + colW / 2}
                y={PAD_T + PLOT_H + 14}
                textAnchor="middle"
                className="fill-gray-500 text-[11px] tabular-nums"
              >
                {formatHour(d.hour)}
              </text>
            ) : null,
          )}

          {/* Strips, so a short bar is as easy to hit as a tall one. They run the
              full height because only leaving the svg clears the hover. */}
          {data.map((d, i) => (
            <rect
              key={d.hour}
              x={bandX(i)}
              y={0}
              width={band}
              height={height}
              fill="transparent"
              onPointerEnter={() => setHover(i)}
            />
          ))}
        </svg>
      )}

      {point && (
        <ChartTooltip
          x={Math.min(Math.max(colX(hover!) + colW / 2, 70), width - 70)}
          y={y(point.count)}
        >
          <TooltipRow
            keyClass="bg-chart-1"
            value={formatCount(point.count)}
            label={`${formatHour(point.hour)} UTC`}
          />
        </ChartTooltip>
      )}

      <DataTable
        caption="Flights by hour of day (UTC)"
        columns={["Hour (UTC)", "Flights"]}
        rows={data.map((d) => [formatHour(d.hour), formatCount(d.count)])}
      />
    </div>
  );
}
