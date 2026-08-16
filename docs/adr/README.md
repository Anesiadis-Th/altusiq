# Architecture Decision Records

This directory contains the Architecture Decision Records (ADRs) for AltusIQ.

ADRs document significant technical decisions, the context that led to them, and their consequences. They serve as a historical record of the project's architectural evolution.

| #                                              | Decision                                                   | Status   |
| ---------------------------------------------- | ---------------------------------------------------------- | -------- |
| [001](001-flight-data-provider.md)             | OpenSky Network as flight data provider                    | Accepted |
| [002](002-backend-hosting-provider.md)         | Fly.io as backend hosting provider                         | Accepted |
| [003](003-realtime-strategy.md)                | SignalR for real-time flight updates                       | Accepted |
| [004](004-map-rendering.md)                    | Mapbox GL JS for map rendering                             | Accepted |
| [005](005-geojson-rendering.md)                | GeoJSON symbol layers over DOM markers                     | Accepted |
| [006](006-storage-strategy.md)                 | Flight-as-track storage with regional scope                | Accepted |
| [007](007-flight-segmentation.md)              | In-memory flight segmentation over Redis                   | Accepted |
| [008](008-flight-enrichment-strategy.md)       | Flight enrichment as a nightly next-day batch              | Accepted |
| [009](009-aircraft-categorisation.md)          | Aircraft categorisation for map icons                      | Rejected |
| [010](010-poll-interval-and-dead-reckoning.md) | Always-on at ~120s polling with client-side dead reckoning | Accepted |
| [011](011-global-live-coverage.md)             | Global live coverage, bbox scoped to persistence           | Accepted |
| [012](012-live-route-lookup.md)                | adsbdb for live route lookup                               | Accepted |
| [013](013-analytics-presentation.md)           | Analytics as an in-map overlay with hand-drawn charts      | Accepted |
| [014](014-backend-test-seams.md)               | Backend test seams over an in-memory database              | Accepted |
| [015](015-airport-dataset-delivery.md)         | Airport datasets as fetched assets, revealed by zoom tier  | Accepted |
| [016](016-mercator-projection.md)              | Mercator projection, because globe drapes line layers      | Accepted |
| [017](017-poller-liveness-health-check.md)     | A health check reporting poller liveness, wired to nothing | Accepted |
