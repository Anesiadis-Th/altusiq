# C4 Context Diagram

The system context shows AltusIQ and its relationship to external actors and systems.

This is the standalone rendering of the diagram embedded in the root [README](../README.md#system-context). Keep the two in step.

```mermaid
graph TB
    User["👤 User
    Watches live global traffic,
    replays Scandinavian flights"]

    AltusIQ["✈️ AltusIQ
    Aviation analytics platform"]

    OpenSky["🛰️ OpenSky Network
    Community ADS-B flight data
    REST API + OAuth2"]

    Adsbdb["🧭 adsbdb
    Callsign → route database"]

    Mapbox["🗺️ Mapbox
    Vector tile map rendering
    WebGL-based"]

    Supabase["🗄️ Supabase
    PostgreSQL + PostGIS
    Historical flight storage"]

    User -->|"Live map, playback,
    search, analytics"| AltusIQ
    AltusIQ -->|"Polls /states/all
    every ~120 s (credit budget)"| OpenSky
    AltusIQ -->|"Resolves callsigns
    to routes (cached)"| Adsbdb
    AltusIQ -->|"Renders map tiles
    and aircraft layers"| Mapbox
    AltusIQ -->|"Reads and writes
    flight tracks"| Supabase

    style AltusIQ fill:#1a56db,stroke:#1e40af,color:#fff
    style User fill:#374151,stroke:#4b5563,color:#fff
    style OpenSky fill:#065f46,stroke:#047857,color:#fff
    style Adsbdb fill:#065f46,stroke:#047857,color:#fff
    style Mapbox fill:#065f46,stroke:#047857,color:#fff
    style Supabase fill:#065f46,stroke:#047857,color:#fff
```

The poll interval is a hard constraint, not a tuning choice — see [ADR-010](adr/010-poll-interval-and-dead-reckoning.md) for the credit arithmetic behind it.
