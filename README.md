# ✈️ AltusIQ

A real-time aviation analytics platform inspired by FlightRadar24. Built as a production-grade portfolio project demonstrating full-stack development, real-time communication, geospatial data storage, and cloud deployment.

**Live:** [altusiq.vercel.app](https://altusiq.vercel.app)

---

## What it does

AltusIQ pulls live ADS-B data from the OpenSky Network every 10 seconds and renders ~11,000 aircraft positions in real time on a WebGL map. Aircraft within the Scandinavian region are segmented into discrete flights, stored in PostGIS, and made available for historical playback — including interpolated altitude, speed, and heading as the track animates.

---

## Architecture

### System Context

```mermaid
graph TB
    User["👤 User
    Views live aircraft, replays
    historical Scandinavian flights"]

    AltusIQ["✈️ AltusIQ
    Aviation analytics platform"]

    OpenSky["🛰️ OpenSky Network
    Community ADS-B flight data
    REST API + OAuth2"]

    Mapbox["🗺️ Mapbox
    Vector tile map rendering
    WebGL-based"]

    Supabase["🗄️ Supabase
    PostgreSQL + PostGIS
    Historical flight storage"]

    User -->|"Views live map,
    replays flights"| AltusIQ
    AltusIQ -->|"Polls /states/all
    every 10 seconds"| OpenSky
    AltusIQ -->|"Renders map tiles
    and aircraft layers"| Mapbox
    AltusIQ -->|"Reads and writes
    flight tracks"| Supabase

    style AltusIQ fill:#1a56db,stroke:#1e40af,color:#fff
    style User fill:#374151,stroke:#4b5563,color:#fff
    style OpenSky fill:#065f46,stroke:#047857,color:#fff
    style Mapbox fill:#065f46,stroke:#047857,color:#fff
    style Supabase fill:#065f46,stroke:#047857,color:#fff
```

### Containers

```mermaid
graph TB
    User["👤 User"]

    subgraph Vercel["Vercel"]
        Frontend["📦 Frontend
        Next.js + TypeScript
        TailwindCSS · Mapbox GL JS
        TanStack Query"]
    end

    subgraph FlyIo["Fly.io (Frankfurt)"]
        API["📦 API
        ASP.NET Core (.NET 8)
        REST endpoints
        SignalR hub"]

        Poller["⚙️ Flight Polling Service
        Polls OpenSky every 10s
        Broadcasts via SignalR
        Feeds ingestion pipeline"]

        Ingestion["⚙️ Flight Ingestion Service
        Filters to Scandinavia bbox
        Segments stream into flights
        Flushes completed tracks to PostGIS"]
    end

    subgraph Supabase["Supabase"]
        Postgres["🗄️ PostgreSQL + PostGIS
        Flights table
        JSONB track points
        GiST spatial index"]
    end

    OpenSky["🛰️ OpenSky Network"]
    Mapbox["🗺️ Mapbox"]

    User -->|"HTTPS"| Frontend
    Frontend -->|"WebSocket (SignalR)"| API
    Frontend -->|"GET /api/flights"| API
    Frontend -->|"Map tiles"| Mapbox
    Poller -->|"GET /api/states/all"| OpenSky
    Poller -->|"SignalR broadcast"| API
    Poller -->|"Aircraft list"| Ingestion
    Ingestion -->|"INSERT completed flights"| Postgres

    style Frontend fill:#7c3aed,stroke:#6d28d9,color:#fff
    style API fill:#1a56db,stroke:#1e40af,color:#fff
    style Poller fill:#1a56db,stroke:#1e40af,color:#fff
    style Ingestion fill:#1a56db,stroke:#1e40af,color:#fff
    style Postgres fill:#065f46,stroke:#047857,color:#fff
    style User fill:#374151,stroke:#4b5563,color:#fff
    style OpenSky fill:#065f46,stroke:#047857,color:#fff
    style Mapbox fill:#065f46,stroke:#047857,color:#fff
    style Vercel fill:#111827,stroke:#374151,color:#9ca3af
    style FlyIo fill:#111827,stroke:#374151,color:#9ca3af
    style Supabase fill:#111827,stroke:#374151,color:#9ca3af
```

---

## Tech Stack

**Frontend** — Next.js, TypeScript, TailwindCSS, TanStack Query, Mapbox GL JS

**Backend** — ASP.NET Core (.NET 8), SignalR, Entity Framework Core, NetTopologySuite, Serilog

**Infrastructure** — Fly.io, Vercel, GitHub Actions, Docker

**Data** — OpenSky Network (OAuth2), PostgreSQL + PostGIS via Supabase, Npgsql

---

## Running Locally

### Prerequisites

- Node.js 20+
- .NET 8 SDK
- An [OpenSky Network](https://opensky-network.org) account with API client credentials
- A [Mapbox](https://mapbox.com) access token
- A [Supabase](https://supabase.com) project with PostGIS enabled

### Backend

```bash
cd backend
dotnet user-secrets set "OpenSky:ClientId" "your_client_id"
dotnet user-secrets set "OpenSky:ClientSecret" "your_client_secret"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=...;Database=postgres;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true"
dotnet ef database update
dotnet run
```

The API starts at `http://localhost:8080`. Verify with `http://localhost:8080/health`.

Use the Supabase **Session pooler** connection string (port 5432) — not the direct connection, which is IPv6-only on the free tier.

### Frontend

```bash
cd frontend
cp .env.local.example .env.local
# Edit .env.local — set NEXT_PUBLIC_MAPBOX_TOKEN and NEXT_PUBLIC_API_URL
npm install
npm run dev
```

Opens at `http://localhost:3000`.

---

## Deployment

The backend deploys to **Fly.io** via GitHub Actions on every push to `master`. The frontend deploys to **Vercel** automatically on push.

Backend secrets are set via `fly secrets set` and never touch the repository. See [ADR-002](docs/adr/002-backend-hosting-provider.md) for why Fly.io was chosen.

---

## Project Status

| Phase | Description                                | Status      |
| ----- | ------------------------------------------ | ----------- |
| 1     | Live map with real-time aircraft positions | ✅ Complete |
| 2     | Historical flight storage and playback     | ✅ Complete |
| 3     | Analytics dashboard                        | 🔜 Planned  |

---

## Architecture Decision Records

Key technical decisions are documented as ADRs in [`docs/adr/`](docs/adr/).

| #                                               | Decision                                            | Status   |
| ----------------------------------------------- | --------------------------------------------------- | -------- |
| [001](docs/adr/001-flight-data-provider.md)     | OpenSky Network as flight data provider             | Accepted |
| [002](docs/adr/002-backend-hosting-provider.md) | Fly.io as backend hosting provider                  | Accepted |
| [003](docs/adr/003-realtime-strategy.md)        | SignalR for real-time flight updates                | Accepted |
| [004](docs/adr/004-map-rendering.md)            | Mapbox GL JS for map rendering                      | Accepted |
| [005](docs/adr/005-geojson-rendering.md)        | GeoJSON symbol layers over DOM markers              | Accepted |
| [006](docs/adr/006-storage-strategy.md)         | Flight-as-track storage model with regional scoping | Accepted |
| [007](docs/adr/007-flight-segmentation.md)      | In-memory flight segmentation over Redis            | Accepted |
