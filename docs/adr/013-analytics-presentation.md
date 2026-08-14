# ADR-013: Analytics as an In-Map Overlay with Hand-Drawn Charts

## Status

Accepted. Supersedes the routed `/analytics` page introduced in `bccac70`, and retires the altitude-band chart that [ADR-011](011-global-live-coverage.md) expected the `MaxAltitude` column to rescue.

## Context

The analytics dashboard was built as an in-map overlay, then moved to its own route. Both the move and the chart set that came with it caused problems.

**The route re-initialises the map.** Navigating to `/analytics` unmounts `FlightMap`, so returning to the map rebuilt the Mapbox instance, re-fetched `airports.geojson`, reconnected SignalR, and discarded pan, zoom, selection and playback state. The overlay was chosen originally for exactly this reason and the reason had not changed.

**Opening it was three waits in series with no feedback.** The route's client chunk was 221 KB gzipped (recharts plus a statically-imported 8,252-row airport code table), there was no `loading.tsx`, and the data request could not start until that chunk had downloaded and mounted. The first leg showed the user nothing at all.

**Several charts were not answerable from the data.** The altitude-band chart bucketed on `MaxAltitude ?? LastAltitude`, and `MaxAltitude` only existed for rows written after the full-track ingestion change — so most of the window still fell back to a touchdown altitude and 56% of flights landed in the bottom band. The chart's shape was an artifact of a migration date, and would change on its own as rows aged out.

**The per-day series lied at both ends.** The window ran `now - 15d` to `now`, so the first and last buckets were partial days — a measured 3,198 and 2,161 against a ~5,300 baseline — drawing a cliff at each end that had nothing to do with traffic. The oldest bucket was doubly wrong: retention purges `ClosedAt < now - 15 days` at 06:00 UTC, so part of that day had already been deleted.

## Decision

**Return analytics to a full-screen overlay, driven by the existing `activePanel` state.** It is a fourth value in the `Panel` union, not a route and not a new boolean (see the mutual-exclusion invariant in `useActivePanel`). The map stays mounted underneath, so opening and closing costs nothing and preserves everything.

**Split the overlay into an eager shell and a lazy body.** `AnalyticsShell` — scrim, header, close button — ships in the map bundle and renders in ~55 ms from the click. `AnalyticsContent` is a `dynamic(ssr:false)` chunk with a skeleton fallback that mirrors the final layout. The panel is therefore never a blank wait; only its contents stream in.

**Start every part of the open cost on hover.** Pointer-enter and focus on the Analytics button preload the content chunk and prefetch both queries. The chunk, the aggregation and the airport code table then resolve in parallel instead of discovering one another in sequence.

**Draw the charts by hand and drop recharts.** Four shapes — a line, a column histogram and two ranked bar lists — do not justify 220 KB gzipped, and the library's default chrome (grid on every chart, a legend for two obvious series, rounded caps, gradient fills) is most of what made the dashboard look generic. The two rank charts became ranked lists with inline bars, which is a better form for named categories anyway: the order is the message and every value is printed rather than read off an axis.

**Serve the airport code table from `public/`, not a static import.** Same rule and same reason as `airports.geojson` in [ADR-005](005-geojson-rendering.md): a static JSON import becomes a JS module parsed by the JS parser inside a content-hashed chunk, re-downloaded on every deploy. As a public asset it is `JSON.parse`d, cached across deploys, and fetched alongside the analytics request rather than gating it.

**Warm the server cache on a timer.** `AnalyticsCacheWarmer` recomputes every 4 minutes against a 5-minute TTL, so the entry is always replaced before it can expire. Measured: 969 ms cold, 0.3 ms warm. With low traffic and a 5-minute TTL, nearly every visitor used to be the one who paid the cold compute.

**Move the window to whole UTC days, and to 14 of them.** `to` is today's UTC midnight, exclusive; `from` is 14 days earlier. Every bucket is now a complete day. 14 rather than 15 because retention measures its 15 days from the moment it runs, so the 15th day back is already partly purged by the time anyone reads it.

**Drop the altitude distribution entirely** rather than repair it. It is not recoverable within the retention window and it was the weakest of the five charts even when correct.

**State the scope and the coverage on the page.** The hero reads "flights tracked through Scandinavian airspace"; a footnote gives the actual box (4–32°E, 54–72°N) and explains why airports outside it appear. The enrichment caveat sits directly under the two cards it qualifies, not in the footnotes, because the counts above it are a sample and saying so is part of reading them. The airport and route cards carry their denominators ("Top 10 of 854 seen", "Top 10 of 2,910 flown") — both come free from aggregations already being computed.

**Crop the per-day axis; never crop a bar axis.** Daily volume moves about ±9% around the mean, so a zero baseline draws a flat line and hides the weekly rhythm the chart exists to show. A line encodes slope and may be cropped when the ticks declare it; the hourly columns encode magnitude as length and stay anchored at zero. `niceRange` and `niceAxis` in `lib/chart.ts` are deliberately separate functions so the two cases cannot be confused.

**Numbers wear the app's monospace, including the hero.** Every figure on the panel uses `font-mono tabular-nums` — the same treatment as the flight panel, the history list and the TopBar counter — with the surrounding prose left in the sans face. General dataviz advice is to set a large standalone number in proportional figures, because equal-width digits read loose at display size. It is overruled here: the app's numeric identity is monospace throughout, and an analytics panel that opted out would be the one screen that looked borrowed. `tracking-tight` on the hero absorbs most of the extra width.

## Alternatives Considered

**Keeping recharts and only restyling.** Would have fixed the generic look but left the 220 KB and the chart set that was chosen for what was easy to aggregate rather than what was worth showing.

**Trimming the airport code table to scheduled-service airports** (~4,000 of 8,252, roughly halving it). Rejected: it saves ~50 KB on an asset that is now cached across deploys, and costs names for any airfield outside that filter that does appear in the data.

**Repairing the altitude chart with finer bands above 8 km.** The binning was the smaller problem; the metric itself was two different measurements concatenated at a migration boundary.

**Snapping to 15 whole days instead of 14.** The oldest bucket would still be eroded by the 06:00 purge, reintroducing exactly the partial-bucket cliff this change removes.

## Consequences

- **Analytics excludes today.** The map shows flights in the air now; the panel's most recent bucket is yesterday. This is stated on the page ("14 complete UTC days") and is the price of every bucket being comparable.
- **Total flights dropped from ~79k to ~74k** purely from the window change. Nothing was lost — the old number included two partial days.
- Frontend JS fell from 916 KB to 699 KB gzipped in total, and the analytics chunk from 221 KB to under 10 KB.
- **The charts are ours to maintain.** Axis ticks, hover, tooltips and the accessible table twin are all hand-written in `components/analytics/`. That is a real cost, paid deliberately: these four shapes are simple and stable, and anything genuinely complex should reconsider a library rather than extend this code.
- The hand-drawn SVG charts need a measured pixel width (`useElementWidth`), so they render nothing on the first frame. The skeleton covers it.
- The chart palette is now two tokens in `globals.css` validated against the card surface. Changing either requires re-running the validator — the comment there says so.
- **Monospace digits are wider, and the y-axis gutter had to grow to fit them.** Measured after the switch, the widest tick label sat 2.9 px from the SVG's left edge at 6.6 px per character, so a six-figure label (`10,000`) would have been clipped — reachable just by lengthening the window, since the hourly totals scale with it. `PAD_L` went from 46 to 54 in both charts.
