"use client";

import {
  ResponsiveContainer,
  BarChart,
  Bar,
  Cell,
  AreaChart,
  Area,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  Legend,
} from "recharts";
import { useAnalytics } from "@/hooks/useAnalytics";
import { airportLabel, airportName } from "@/lib/airports";
import { Analytics, Route } from "@/types/analytics";

const COLOR_DEPARTURES = "#3b82f6"; // blue-500
const COLOR_ARRIVALS = "#38bdf8"; // sky-400
const COLOR_ACCENT = "#6366f1"; // indigo-500
const GRID = "#1f2937"; // gray-800
const AXIS = "#9ca3af"; // gray-400

const TOOLTIP_STYLE = {
  backgroundColor: "#0b1220",
  border: "1px solid #374151",
  borderRadius: "0.5rem",
  color: "#e5e7eb",
} as const;

const TICK = { fill: AXIS, fontSize: 12 } as const;

export default function AnalyticsDashboard({
  onClose,
}: {
  onClose: () => void;
}) {
  const { data, isLoading, error } = useAnalytics();

  if (isLoading) {
    return (
      <Overlay>
        <div className="min-h-dvh flex items-center justify-center text-gray-400">
          Loading analytics…
        </div>
      </Overlay>
    );
  }

  if (error || !data) {
    return (
      <Overlay>
        <div className="min-h-dvh flex flex-col items-center justify-center gap-3">
          <p className="text-red-400">Failed to load analytics.</p>
          <button
            onClick={onClose}
            className="text-blue-400 hover:text-blue-300 text-sm"
          >
            ← Back to map
          </button>
        </div>
      </Overlay>
    );
  }

  return (
    <Overlay>
      <Header data={data} onClose={onClose} />
      <StatCards data={data} />

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-5 mt-5">
        <ChartCard title="Busiest airports" subtitle="Departures + arrivals">
          <BusiestAirportsChart data={data} />
        </ChartCard>

        <ChartCard title="Top routes" subtitle="Origin → destination">
          <TopRoutesChart data={data} />
        </ChartCard>

        <ChartCard title="Flights per day" subtitle="Closed flights per UTC day">
          <FlightsPerDayChart data={data} />
        </ChartCard>

        <ChartCard title="Flights by hour" subtitle="Hour of day (UTC)">
          <FlightsPerHourChart data={data} />
        </ChartCard>

        <ChartCard
          title="Altitude distribution"
          subtitle="By last reported altitude"
        >
          <AltitudeBandsChart data={data} />
        </ChartCard>
      </div>
    </Overlay>
  );
}

function Overlay({ children }: { children: React.ReactNode }) {
  return (
    <div className="absolute inset-0 z-40 overflow-y-auto bg-gray-950/75 backdrop-blur-md">
      <div className="px-4 py-4 sm:px-6 sm:py-6 max-w-7xl mx-auto">{children}</div>
    </div>
  );
}

function Header({
  data,
  onClose,
}: {
  data: Analytics;
  onClose: () => void;
}) {
  return (
    <div className="flex items-center justify-between mb-6">
      <div>
        <h1 className="text-2xl font-semibold text-white">Analytics</h1>
        <p className="text-gray-500 text-sm mt-0.5">
          Last {data.range_days} days · {data.total_flights.toLocaleString()}{" "}
          flights
        </p>
      </div>
      <button
        onClick={onClose}
        className="bg-gray-900/90 border border-gray-700/50 rounded-lg px-3 py-2 text-sm text-gray-300 hover:text-white hover:border-gray-500/50 transition-colors"
      >
        ← Live map
      </button>
    </div>
  );
}

function StatCards({ data }: { data: Analytics }) {
  const enrichedPct =
    data.total_flights > 0
      ? Math.round((data.enriched_flights / data.total_flights) * 100)
      : 0;

  const busiest = data.busiest_airports[0];

  const peak = data.flights_per_hour.reduce(
    (best, h) => (h.count > best.count ? h : best),
    { hour: 0, count: 0 },
  );

  return (
    <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
      <StatCard
        label="Total flights"
        value={data.total_flights.toLocaleString()}
      />
      <StatCard
        label="Enriched"
        value={`${enrichedPct}%`}
        hint={`${data.enriched_flights.toLocaleString()} with route data`}
      />
      <StatCard
        label="Busiest airport"
        value={busiest ? airportLabel(busiest.icao) : "—"}
        hint={
          busiest
            ? [airportName(busiest.icao), `${busiest.total.toLocaleString()} movements`]
                .filter(Boolean)
                .join(" · ")
            : undefined
        }
      />
      <StatCard
        label="Peak hour"
        value={`${String(peak.hour).padStart(2, "0")}:00`}
        hint={`${peak.count.toLocaleString()} flights · UTC`}
      />
    </div>
  );
}

function StatCard({
  label,
  value,
  hint,
}: {
  label: string;
  value: string;
  hint?: string;
}) {
  return (
    <div className="bg-gray-900/70 border border-gray-800 rounded-xl px-4 py-3">
      <p className="text-gray-500 text-xs uppercase tracking-wide">{label}</p>
      <p className="text-white text-2xl font-semibold mt-1">{value}</p>
      {hint && <p className="text-gray-600 text-xs mt-1">{hint}</p>}
    </div>
  );
}

function ChartCard({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle: string;
  children: React.ReactNode;
}) {
  return (
    <div className="bg-gray-900/70 border border-gray-800 rounded-xl p-4">
      <div className="mb-3">
        <h2 className="text-white font-medium">{title}</h2>
        <p className="text-gray-500 text-xs">{subtitle}</p>
      </div>
      <div className="h-72">{children}</div>
    </div>
  );
}

function BusiestAirportsChart({ data }: { data: Analytics }) {
  if (data.busiest_airports.length === 0) return <EmptyChart />;

  return (
    <ResponsiveContainer width="100%" height="100%">
      <BarChart
        data={data.busiest_airports}
        layout="vertical"
        margin={{ left: 8, right: 16, top: 4, bottom: 4 }}
      >
        <CartesianGrid stroke={GRID} horizontal={false} />
        <XAxis type="number" tick={TICK} stroke={GRID} />
        <YAxis
          type="category"
          dataKey="icao"
          tick={TICK}
          stroke={GRID}
          width={48}
          tickFormatter={(icao: string) => airportLabel(icao)}
        />
        <Tooltip
          contentStyle={TOOLTIP_STYLE}
          cursor={{ fill: "#ffffff10" }}
          labelFormatter={(icao: unknown) =>
            airportName(String(icao)) ?? airportLabel(String(icao))
          }
        />
        <Legend wrapperStyle={{ color: AXIS, fontSize: 12 }} />
        <Bar
          dataKey="departures"
          stackId="a"
          fill={COLOR_DEPARTURES}
          name="Departures"
        />
        <Bar
          dataKey="arrivals"
          stackId="a"
          fill={COLOR_ARRIVALS}
          name="Arrivals"
          radius={[0, 4, 4, 0]}
        />
      </BarChart>
    </ResponsiveContainer>
  );
}

function TopRoutesChart({ data }: { data: Analytics }) {
  if (data.top_routes.length === 0) return <EmptyChart />;

  const rows = data.top_routes.map((r: Route) => ({
    label: `${airportLabel(r.departure)} → ${airportLabel(r.arrival)}`,
    count: r.count,
  }));

  return (
    <ResponsiveContainer width="100%" height="100%">
      <BarChart
        data={rows}
        layout="vertical"
        margin={{ left: 8, right: 16, top: 4, bottom: 4 }}
      >
        <CartesianGrid stroke={GRID} horizontal={false} />
        <XAxis type="number" tick={TICK} stroke={GRID} />
        <YAxis
          type="category"
          dataKey="label"
          tick={TICK}
          stroke={GRID}
          width={110}
        />
        <Tooltip contentStyle={TOOLTIP_STYLE} cursor={{ fill: "#ffffff10" }} />
        <Bar
          dataKey="count"
          fill={COLOR_ACCENT}
          name="Flights"
          radius={[0, 4, 4, 0]}
        />
      </BarChart>
    </ResponsiveContainer>
  );
}

function FlightsPerDayChart({ data }: { data: Analytics }) {
  return (
    <ResponsiveContainer width="100%" height="100%">
      <AreaChart
        data={data.flights_per_day}
        margin={{ left: 4, right: 16, top: 4, bottom: 4 }}
      >
        <defs>
          <linearGradient id="dayFill" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stopColor={COLOR_DEPARTURES} stopOpacity={0.5} />
            <stop offset="100%" stopColor={COLOR_DEPARTURES} stopOpacity={0} />
          </linearGradient>
        </defs>
        <CartesianGrid stroke={GRID} vertical={false} />
        <XAxis
          dataKey="date"
          tick={TICK}
          stroke={GRID}
          tickFormatter={(value: string) =>
            new Date(value).toLocaleDateString("en-US", {
              month: "short",
              day: "numeric",
            })
          }
        />
        <YAxis tick={TICK} stroke={GRID} width={44} />
        <Tooltip contentStyle={TOOLTIP_STYLE} cursor={{ stroke: AXIS }} />
        <Area
          type="monotone"
          dataKey="count"
          stroke={COLOR_DEPARTURES}
          strokeWidth={2}
          fill="url(#dayFill)"
          name="Flights"
        />
      </AreaChart>
    </ResponsiveContainer>
  );
}

function FlightsPerHourChart({ data }: { data: Analytics }) {
  return (
    <ResponsiveContainer width="100%" height="100%">
      <BarChart
        data={data.flights_per_hour}
        margin={{ left: 4, right: 16, top: 4, bottom: 4 }}
      >
        <CartesianGrid stroke={GRID} vertical={false} />
        <XAxis
          dataKey="hour"
          tick={TICK}
          stroke={GRID}
          tickFormatter={(value: number) => String(value).padStart(2, "0")}
          interval={1}
        />
        <YAxis tick={TICK} stroke={GRID} width={44} />
        <Tooltip
          contentStyle={TOOLTIP_STYLE}
          cursor={{ fill: "#ffffff10" }}
          labelFormatter={(label: unknown) =>
            `${String(label).padStart(2, "0")}:00 UTC`
          }
        />
        <Bar dataKey="count" fill={COLOR_ACCENT} name="Flights" radius={[3, 3, 0, 0]} />
      </BarChart>
    </ResponsiveContainer>
  );
}

function AltitudeBandsChart({ data }: { data: Analytics }) {
  return (
    <ResponsiveContainer width="100%" height="100%">
      <BarChart
        data={data.altitude_bands}
        margin={{ left: 4, right: 16, top: 4, bottom: 4 }}
      >
        <CartesianGrid stroke={GRID} vertical={false} />
        <XAxis dataKey="label" tick={TICK} stroke={GRID} interval={0} />
        <YAxis tick={TICK} stroke={GRID} width={44} />
        <Tooltip contentStyle={TOOLTIP_STYLE} cursor={{ fill: "#ffffff10" }} />
        <Bar dataKey="count" name="Flights" radius={[3, 3, 0, 0]}>
          {data.altitude_bands.map((_, i) => (
            <Cell
              key={i}
              fill={`rgba(56, 189, 248, ${0.35 + (i / data.altitude_bands.length) * 0.65})`}
            />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  );
}

function EmptyChart() {
  return (
    <div className="h-full flex items-center justify-center text-gray-600 text-sm">
      No data in this window
    </div>
  );
}
