# Architecture Decision Records

This directory contains the Architecture Decision Records (ADRs) for AltusIQ.

ADRs document significant technical decisions, the context that led to them, and their consequences. They serve as a historical record of the project's architectural evolution.

| #                                        | Decision                                      | Status   |
| ---------------------------------------- | --------------------------------------------- | -------- |
| [001](001-flight-data-provider.md)       | OpenSky Network as flight data provider       | Accepted |
| [002](002-backend-hosting-provider.md)   | Fly.io as backend hosting provider            | Accepted |
| [003](003-realtime-strategy.md)          | SignalR for real-time flight updates          | Accepted |
| [004](004-map-rendering.md)              | Mapbox GL JS for map rendering                | Accepted |
| [005](005-geojson-rendering.md)          | GeoJSON symbol layers over DOM markers        | Accepted |
| [006](006-storage-strategy.md)           | Flight-as-track storage with regional scope   | Accepted |
| [007](007-flight-segmentation.md)        | In-memory flight segmentation over Redis      | Accepted |
| [008](008-flight-enrichment-strategy.md) | Flight enrichment as a nightly next-day batch | Accepted |
