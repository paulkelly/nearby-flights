# Nearby Flights

Pick a spot on a map, choose a radius, and see live information for every aircraft flying inside that circle: callsign, aircraft type, altitude, speed, heading and distance from you.

> **Status:** early development. The backend API is working; the map UI is next. See the [roadmap](#roadmap).

## What it will do

- **Pick a location** by clicking on an OpenStreetMap map.
- **Choose a radius** with a slider and see the search circle drawn on the map.
- **See aircraft live** as markers pointing in their direction of travel, plus a list sorted by distance.
- **Smooth movement** between updates by extrapolating each aircraft's position from its speed and heading.
- Later: real-time push updates, saved locations, and a choice of flight data sources.

Flight data comes from [ADS-B](https://en.wikipedia.org/wiki/Automatic_Dependent_Surveillance%E2%80%93Broadcast) broadcasts collected by volunteer receivers. ADS-B gives an aircraft's position, altitude, speed and identity, but **not** its origin or destination.

## How it works

```
React map (web/)  ──HTTP──►  FlightRadar.Api  ──►  IFlightDataProvider  ──►  adsb.lol
```

The browser never calls the flight data source directly. The backend:

- converts units (the public API uses kilometres; adsb.lol uses nautical miles),
- maps third-party data into the app's own model, so the source can be swapped without changing the frontend,
- filters aircraft to the exact circle using great-circle (haversine) distance and sorts them nearest first,
- returns errors as [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) JSON: `400` for bad input, `502` when the data source is down.

### API

```http
GET /api/flights?lat=53.4213&lon=-6.2701&radiusKm=30
```

| Parameter | Meaning |
|---|---|
| `lat` | Latitude of the centre, -90 to 90 |
| `lon` | Longitude of the centre, -180 to 180 |
| `radiusKm` | Search radius in kilometres, greater than 0 and at most 463 |

Example response (trimmed):

```json
{
  "lat": 53.4213,
  "lon": -6.2701,
  "radiusKm": 30,
  "count": 1,
  "fetchedAt": "2026-11-03T13:55:00Z",
  "flights": [
    {
      "hex": "4cad7b",
      "callsign": "EIN58P",
      "registration": "EI-NSB",
      "aircraftType": "A20N",
      "lat": 53.448257,
      "lon": -6.49551,
      "positionTimestamp": "2026-11-03T13:54:59.5Z",
      "distanceKm": 15.23,
      "altitudeFt": 2200,
      "onGround": false,
      "groundSpeedKt": 165,
      "trackDeg": 89.65,
      "verticalRateFpm": -704
    }
  ]
}
```

When running in Development, interactive API docs are at `/scalar` and the OpenAPI document is at `/openapi/v1.json`.

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Node.js (once the frontend exists)

No API keys are needed: adsb.lol is free and keyless.

### Run the API

```bash
dotnet run --project src/FlightRadar.Api
```

Then open http://localhost:5123/scalar to explore the API, or use the sample requests in `src/FlightRadar.Api/FlightRadar.Api.http` from Rider or VS Code.

### Run the tests

```bash
dotnet test
```

The tests never call the real flight data API: HTTP traffic is faked, and endpoint tests run the whole app in memory.

## Project layout

```
src/FlightRadar.Api/      ASP.NET Core Web API: endpoints, configuration, data providers
src/FlightRadar.Core/     domain model, geo maths, provider interface (no web dependencies)
tests/FlightRadar.Tests/  xUnit unit and endpoint tests
web/                      React + TypeScript frontend: map, aircraft markers, flight list
```

## Tech stack

| Area | Choice |
|---|---|
| Backend | ASP.NET Core minimal APIs, .NET 10, C# |
| Tests | xUnit, `WebApplicationFactory` |
| API docs | OpenAPI + Scalar |
| Frontend | React, TypeScript, Vite, react-leaflet, TanStack Query |
| Containers | Docker, Docker Compose |

## Roadmap

- [x] **0. Setup:** solution layout, shared build settings
- [x] **1. Backend:** `/api/flights`, adsb.lol provider, tests, OpenAPI docs
- [ ] **2. Map UI:** click to set the centre, radius slider, circle, aircraft markers and list
- [ ] **3. Docker:** multi-stage images for the API and web (nginx), Compose
- [ ] **4. Hardening:** retries, caching (memory, then Redis), structured logging, health checks, rate limiting, OpenSky as a second provider
- [ ] **5. Enrichment:** airline names and probable routes for each flight
- [ ] **6. Real-time:** background polling with SignalR push; saved locations in Postgres
- [ ] **7. CI/CD:** GitHub Actions, optional deployment to Azure Container Apps

## About this project

This is a learning project, built to practise corporate-style .NET backend development, React with TypeScript, Docker and CI/CD, with an AI coding assistant as a pair programmer. Commits are kept small and focused so the history reads as a step-by-step build.

## Data and attribution

- Flight data: [adsb.lol](https://adsb.lol), a community ADS-B network whose data is published under the [Open Database License (ODbL)](https://opendatacommons.org/licenses/odbl/).
- Map tiles: © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors.

Live positions can be delayed or incomplete, and not every aircraft broadcasts ADS-B. **Not for navigation or any safety-related use.**
