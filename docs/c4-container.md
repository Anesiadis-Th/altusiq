# C4 Container Diagram

The container diagram shows the major deployable units inside AltusIQ and how they communicate.

This is the standalone rendering of the diagram embedded in the root [README](../README.md#containers). Keep the two in step.

```mermaid
graph TB
    User["👤 User"]

    subgraph Vercel["Vercel"]
        Frontend["📦 Frontend
        Next.js + TypeScript
        Mapbox GL JS
        Dead-reckons aircraft between polls"]
    end

    subgraph FlyIo["Fly.io (Frankfurt, always-on)"]
        API["📦 API
        ASP.NET Core (.NET 8)
        REST endpoints · SignalR hub
        Route lookup (adsbdb, cached)"]

        Poller["⚙️ Flight Polling Service
        Polls OpenSky every 120 s
        Broadcasts global snapshot
        Feeds ingestion pipeline"]

        Ingestion["⚙️ Flight Ingestion Service
        In-memory live trails (global)
        Segments Scandinavian traffic
        into flights → PostGIS"]

        Workers["⚙️ Nightly workers
        04:00 UTC route enrichment
        06:00 UTC retention purge (15 d)"]
    end

    subgraph Supabase["Supabase"]
        Postgres["🗄️ PostgreSQL + PostGIS
        Flights table
        JSONB track points
        GiST spatial index"]
    end

    OpenSky["🛰️ OpenSky Network"]
    Adsbdb["🧭 adsbdb"]
    Mapbox["🗺️ Mapbox"]

    User -->|"HTTPS"| Frontend
    Frontend -->|"WebSocket (SignalR)"| API
    Frontend -->|"REST: history, tracks,
    routes, analytics"| API
    Frontend -->|"Map tiles"| Mapbox
    Poller -->|"GET /api/states/all"| OpenSky
    Poller -->|"SignalR broadcast"| API
    Poller -->|"Aircraft list"| Ingestion
    Ingestion -->|"INSERT completed flights"| Postgres
    Workers -->|"GET /flights/all
    (next-day batch)"| OpenSky
    Workers -->|"UPDATE / DELETE"| Postgres
    API -->|"GET /v0/callsign/…"| Adsbdb

    style Frontend fill:#7c3aed,stroke:#6d28d9,color:#fff
    style API fill:#1a56db,stroke:#1e40af,color:#fff
    style Poller fill:#1a56db,stroke:#1e40af,color:#fff
    style Ingestion fill:#1a56db,stroke:#1e40af,color:#fff
    style Workers fill:#1a56db,stroke:#1e40af,color:#fff
    style Postgres fill:#065f46,stroke:#047857,color:#fff
    style User fill:#374151,stroke:#4b5563,color:#fff
    style OpenSky fill:#065f46,stroke:#047857,color:#fff
    style Adsbdb fill:#065f46,stroke:#047857,color:#fff
    style Mapbox fill:#065f46,stroke:#047857,color:#fff
    style Vercel fill:#111827,stroke:#374151,color:#9ca3af
    style FlyIo fill:#111827,stroke:#374151,color:#9ca3af
    style Supabase fill:#111827,stroke:#374151,color:#9ca3af
```

Every background worker — the poller, nightly enrichment, the retention purge, and the analytics cache warmer — runs in-process as a `BackgroundService` on a single always-on machine. Nothing is triggered externally, and in-progress tracks live only in memory, which is why a needless redeploy splits every airborne flight into two database rows ([ADR-007](adr/007-flight-segmentation.md)).
