"use client";

import { useAnalytics } from "@/hooks/useAnalytics";
import { useAirportCodes } from "@/hooks/useAirportCodes";
import { airportLabel, airportName, type AirportCodes } from "@/lib/airports";
import { formatCount, formatUtcDay } from "@/lib/chart";
import { Analytics } from "@/types/analytics";
import { Card, CardBody, CardHeader } from "@/components/ui/Card";
import AnalyticsSkeleton from "./AnalyticsSkeleton";
import DailyFlightsChart from "./DailyFlightsChart";
import HourlyFlightsChart from "./HourlyFlightsChart";
import RankedBars, { RankedRow } from "./RankedBars";
import { Legend } from "./ChartParts";

export default function AnalyticsContent() {
  const { data, isLoading, error } = useAnalytics();
  // isPending, not isError-checking: it clears on failure too, so a code table
  // that never arrives falls back to raw ICAOs instead of pinning the skeleton.
  const { data: codes, isPending: codesPending } = useAirportCodes();

  if (isLoading || codesPending) return <AnalyticsSkeleton />;

  if (error || !data) {
    return (
      <p className="py-10 text-center text-red-400 text-[13px]">
        Failed to load analytics.
      </p>
    );
  }

  return <Dashboard data={data} codes={codes} />;
}

function Dashboard({
  data,
  codes,
}: {
  data: Analytics;
  codes: AirportCodes | undefined;
}) {
  const perDay = Math.round(data.total_flights / data.range_days);
  const firstDay = data.flights_per_day[0]?.date;
  const lastDay = data.flights_per_day.at(-1)?.date;
  const enrichedPct =
    data.total_flights > 0
      ? Math.round((data.enriched_flights / data.total_flights) * 100)
      : 0;

  const airportRows: RankedRow[] = data.busiest_airports.map((airport) => ({
    id: airport.icao,
    label: airportLabel(codes, airport.icao),
    caption: airportName(codes, airport.icao),
    total: airport.total,
    segments: [
      {
        keyClass: "bg-chart-1",
        label: "Departures",
        value: airport.departures,
      },
      { keyClass: "bg-chart-2", label: "Arrivals", value: airport.arrivals },
    ],
  }));

  // No caption here. The airports card next to this one already names them.
  const routeRows: RankedRow[] = data.top_routes.map((route) => ({
    id: `${route.departure}-${route.arrival}`,
    label: `${airportLabel(codes, route.departure)} → ${airportLabel(codes, route.arrival)}`,
    total: route.count,
  }));

  return (
    <>
      <section>
        <p className="text-white text-5xl font-semibold font-mono tabular-nums tracking-tight leading-none">
          {formatCount(data.total_flights)}
        </p>
        <p className="mt-2.5 text-gray-300 text-sm">
          flights tracked through Scandinavian airspace
        </p>
        <p className="mt-1 text-gray-500 text-xs">
          <Num>{data.range_days}</Num> complete UTC days
          {firstDay && lastDay && (
            <>
              <span className="mx-1.5">·</span>
              <Num>{formatUtcDay(firstDay)}</Num> –{" "}
              <Num>{formatUtcDay(lastDay)}</Num>
            </>
          )}
        </p>
      </section>

      <div className="mt-6 flex flex-col gap-4">
        <Card>
          <CardHeader
            title="Flights per day"
            subtitle={
              <>
                Averaging <Num>{formatCount(perDay)}</Num> a day
              </>
            }
          />
          <CardBody>
            <DailyFlightsChart data={data.flights_per_day} />
          </CardBody>
        </Card>

        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <Card>
            <CardHeader
              title="Busiest airports"
              subtitle={
                <>
                  Top 10 of <Num>{formatCount(data.distinct_airports)}</Num>{" "}
                  seen
                </>
              }
              right={
                <Legend
                  items={[
                    { keyClass: "bg-chart-1", label: "Departures" },
                    { keyClass: "bg-chart-2", label: "Arrivals" },
                  ]}
                />
              }
            />
            <CardBody>
              {airportRows.length > 0 ? (
                <RankedBars
                  rows={airportRows}
                  tableCaption="Busiest airports"
                />
              ) : (
                <Empty />
              )}
            </CardBody>
          </Card>

          <Card>
            <CardHeader
              title="Top routes"
              subtitle={
                <>
                  Top 10 of <Num>{formatCount(data.distinct_routes)}</Num> flown
                </>
              }
            />
            <CardBody>
              {routeRows.length > 0 ? (
                <RankedBars rows={routeRows} tableCaption="Top routes" />
              ) : (
                <Empty />
              )}
            </CardBody>
          </Card>
        </div>

        <p className="text-gray-500 text-xs leading-relaxed">
          Airports and routes are known for the <Num>{enrichedPct}%</Num> of
          flights (<Num>{formatCount(data.enriched_flights)}</Num>) that
          OpenSky&rsquo;s next-day batch could resolve to a departure and an
          arrival. Real movement counts are higher than the figures above.
        </p>

        <Card>
          <CardHeader
            title="Flights by hour"
            subtitle="All days combined, by the hour each track ended (UTC)"
          />
          <CardBody>
            <HourlyFlightsChart data={data.flights_per_hour} />
          </CardBody>
        </Card>
      </div>

      <div className="mt-6 flex flex-col gap-2 border-t border-line pt-4">
        <Footnote term="Scope">
          A flight is recorded when at least two of its position reports fall
          inside <Num>4–32°E, 54–72°N</Num>. The rest of its track is kept
          either way, so airports well outside Scandinavia appear whenever a
          tracked flight began or ended there.
        </Footnote>
        <Footnote term="Timing">
          All times UTC. A flight counts on the day and hour its track ended,
          which is close to landing, since low-altitude reception normally drops
          out shortly before touchdown.
        </Footnote>
      </div>
    </>
  );
}

function Num({ children }: { children: React.ReactNode }) {
  return <span className="font-mono tabular-nums">{children}</span>;
}

function Footnote({
  term,
  children,
}: {
  term: string;
  children: React.ReactNode;
}) {
  return (
    <p className="text-gray-600 text-xs leading-relaxed">
      <span className="text-gray-500 font-medium">{term}</span>
      <span className="mx-1.5">·</span>
      {children}
    </p>
  );
}

function Empty() {
  return (
    <p className="py-6 text-center text-gray-600 text-[13px]">
      No airport data in this window yet.
    </p>
  );
}
