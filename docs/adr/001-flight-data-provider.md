# ADR-001: OpenSky Network as Flight Data Provider

## Status

Accepted

## Context

AltusIQ requires a source of live aircraft position data to render flights on a map in real time. The data must include at minimum: position (lat/lng), altitude, speed, heading, and a callsign or identifier.

I evaluated three options:

**OpenSky Network**

- Free, community-driven ADS-B aggregation network
- REST API returning up to ~11,000 live aircraft per request
- Authenticated access via OAuth2 client credentials (changed from basic auth in March 2026)
- No per-request billing
- Known to block requests from major cloud provider IP ranges (AWS, GCP) due to abuse

**AviationStack**

- Commercial API with a free tier
- Free tier limited to 100 requests/month and ~16 aircraft with live position data
- Simple API key authentication
- No IP restrictions

**ADS-B Exchange**

- Community-driven like OpenSky
- Requires a paid RapidAPI subscription for meaningful access
- Higher rate limits on paid tiers

## Decision

I chose **OpenSky Network** as the flight data provider.

## Consequences

### Positive

- 11,000+ aircraft per request creates a visually compelling, production-realistic experience
- No request limits for authenticated users beyond a 10-second polling interval
- Zero cost for data access
- OAuth2 implementation demonstrates a real-world auth flow on the backend

### Negative

- OpenSky blocks requests from hyperscaler IPs (AWS, GCP), which constrains hosting choices — the backend cannot run on platforms backed by these providers (see ADR-002)
- Data quality depends on community ADS-B receiver coverage, so some regions have sparse data
- The OAuth2 token endpoint and API can be intermittently slow or unreliable

### Risks

- If OpenSky changes their access policy or deprecates the free tier, the data source will need to be replaced. The polling service is isolated behind an interface, so the blast radius of a swap is contained to one service class.
