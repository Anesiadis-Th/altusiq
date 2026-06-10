# ADR-003: SignalR for Real-Time Flight Updates

## Status

Accepted

## Context

The frontend needs to display aircraft positions that update every 10 seconds without manual page refreshes. There are three common approaches for pushing data from server to client:

**HTTP Polling**
The frontend sends a GET request to the backend on a timer (e.g. every 10 seconds) and replaces the map data with each response.

**Server-Sent Events (SSE)**
The server maintains a one-way HTTP stream to the client. The backend pushes new data down the stream whenever it's available.

**WebSockets (via SignalR)**
A full-duplex persistent connection between client and server. SignalR is Microsoft's abstraction over WebSockets with automatic fallback to SSE and long-polling when WebSockets aren't available.

## Decision

I chose **SignalR** (WebSockets with automatic transport fallback).

## Consequences

### Positive

- SignalR is built into ASP.NET Core — no additional dependencies on the backend
- Automatic transport negotiation means it works even in restrictive network environments (falls back to SSE, then long-polling)
- The `IHubContext<T>` pattern allows the background polling service to broadcast to connected clients cleanly, without coupling the hub to the data ingestion logic
- Native JavaScript client (`@microsoft/signalr`) with reconnection support
- Demonstrates real-time systems knowledge on the portfolio, which is a frequently assessed skill in backend interviews

### Negative

- SignalR requires its own JSON serialization configuration independent of the MVC controller pipeline — this caused a bug where property names were serialized in different casing over SignalR vs REST endpoints
- CORS configuration must explicitly allow credentials and specify exact origins (no wildcards) for SignalR to function cross-origin
- React StrictMode's double-mount behaviour in development causes SignalR to attempt connection, disconnect immediately, then reconnect — producing misleading console errors that required a `stopped` flag pattern to handle cleanly

### Why Not HTTP Polling

HTTP polling would have been simpler to implement but creates unnecessary load. With 11,000 aircraft, each response payload is roughly 2-3MB. Polling means the client requests this regardless of whether the data has changed. With SignalR, the server pushes only when new data arrives, and the persistent connection avoids repeated TCP/TLS handshake overhead.
