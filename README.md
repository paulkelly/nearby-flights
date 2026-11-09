# Nearby Flights

Pick a spot on a map, choose a radius, and see live information for every aircraft flying inside that circle: callsign, aircraft type, altitude, speed, heading and distance from you.

> **Status:** the API and map UI work on desktop and phones, the app installs as a PWA, and the whole thing runs with one `docker compose` command. Backend hardening is complete: resilience, caching (including Redis), structured logging, health checks, rate limiting and a second data source (OpenSky) as a fallback. Enrichment (airlines and routes) is next. See the [roadmap](#roadmap).

## What it does

- **Pick a location** by clicking the OpenStreetMap map (long-press on touch screens), or use **📍 Use my location**.
- **Choose a radius** with a slider and see the search circle drawn on the map.
- **See aircraft live** as plane icons pointing in their direction of travel, refreshed every 10 seconds, plus a list sorted by distance. Click a plane or a list row for details; hovering highlights it in both places.
- **Smooth movement** between updates: each aircraft's position is extrapolated from its speed and track (dead reckoning), the same trick games use to smooth networked players.
- **Works on phones:** the sidebar becomes a bottom sheet, and the app can be **installed** to the home screen as a Progressive Web App. It opens in its own window, and the app itself loads even offline (live data needs a connection).
- Later: airline and route details, real-time push updates, saved locations, and a choice of flight data sources.

Flight data comes from [ADS-B](https://en.wikipedia.org/wiki/Automatic_Dependent_Surveillance%E2%80%93Broadcast) broadcasts collected by volunteer receivers. ADS-B gives an aircraft's position, altitude, speed and identity, but **not** its origin or destination.

## How it works

```
React map (web/)  ──HTTP──►  FlightRadar.Api  ──►  IFlightDataProvider  ──►  adsb.lol
                                                                       └─ if it fails ──►  OpenSky
```

The browser never calls the flight data source directly. The backend:

- converts units (the public API uses kilometres; adsb.lol uses nautical miles, OpenSky metres and a bounding box),
- falls back to a second source, the [OpenSky Network](https://opensky-network.org), when adsb.lol is unavailable (or can use OpenSky as the main source),
- maps third-party data into the app's own model, so the source can be swapped without changing the frontend,
- filters aircraft to the exact circle using great-circle (haversine) distance and sorts them nearest first,
- retries brief failures of the data source with timeouts and a circuit breaker, so a struggling source gets a rest instead of more requests,
- caches each area's answer for a few seconds, so identical requests (several tabs or users) share one upstream call,
- logs structured events (one summary per request, plus cache misses and upstream calls) without recording users' exact locations,
- limits each client to a fair request rate (bursts of 20, then one request every 2 s) and caps its own concurrent calls to the data source, so it stays a good neighbour to a free community service,
- returns errors as [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) JSON: `400` for bad input, `429` when a client sends too many requests (with `Retry-After`), `502` when the data source is down.

### API

```http
GET /api/flights?lat=53.4213&lon=-6.2701&radiusKm=30
```

| Parameter | Meaning |
|---|---|
| `lat` | Latitude of the centre, -90 to 90 |
| `lon` | Longitude of the centre, -180 to 180 |
| `radiusKm` | Search radius in kilometres, greater than 0 and at most 250 |

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

No API keys are needed: adsb.lol is free and keyless, and OpenSky (the fallback) works anonymously with a smaller daily allowance. To give OpenSky your own free account's allowance, see [OpenSky credentials](#opensky-credentials-optional) below.

### Quick start with Docker

The only prerequisite is [Docker Desktop](https://www.docker.com/products/docker-desktop/). From the repo root:

```bash
docker compose up --build
```

Open http://localhost:8080. That runs three containers: **web** (nginx serving the built React app), **api** (the ASP.NET Core API) and **redis** (a shared cache). Only nginx is exposed to the network; it forwards `/api` requests to the API over Compose's private network. Redis is published on `127.0.0.1:6379` only, for local tools. `docker compose ps` shows each container's health: the API's comes from its `/health/ready` endpoint.

The API can run as several instances sharing one Redis cache: `docker compose up --build --scale api=2`. See [docs/redis-lab.md](docs/redis-lab.md) for hands-on Redis exercises.

The API logs structured events (JSON lines: `docker compose logs api`). To browse and query them, start the optional [Seq](https://datalust.co/seq) log viewer (free for individual use) with the `logs` profile, then open http://localhost:5341 and try a query such as `StatusCode >= 400` or `ElapsedMs > 300`:

```bash
docker compose --profile logs up --build
```

```bash
docker compose down                  # stop and remove the containers
docker compose --profile logs down   # the same if you started with --profile logs (a plain down leaves Seq running)
```

To use a different port, set `WEB_PORT` (e.g. `WEB_PORT=9090` in a `.env` file next to `compose.yaml`). The containers run in Production mode, so the API docs at `/scalar` are off there.

### Try it on your phone

- **Layout only:** on the same Wi-Fi, open `http://<your PC's IP>:8080` (find the IP with `ipconfig`). On Android, Chrome needs the **Nearby devices** permission to reach local network addresses.
- **Install and "Use my location":** these need HTTPS. A Cloudflare quick tunnel (the free `cloudflared` tool, no account needed) gives a temporary public HTTPS address:

  ```bash
  cloudflared tunnel --url http://localhost:8080
  ```

  Open the printed `https://….trycloudflare.com` address on the phone. The app is publicly reachable while the tunnel runs; stop it with Ctrl+C, and uninstall the test app afterwards (it's tied to that random address).

### Local development

For day-to-day work, run the API and the frontend directly for fast rebuilds and hot reload. You'll need:

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js](https://nodejs.org/) 22 or newer

#### Run the API

```bash
dotnet run --project src/FlightRadar.Api
```

This uses the `http` launch profile, so the API listens on port 5123 in the Development environment and reads [user-secrets](#opensky-credentials-optional). If Redis is running (`docker compose up -d redis`), the API uses it as a second-level cache; if not, it runs on its in-memory cache alone. Likewise, if Seq is running (`docker compose --profile logs up -d seq`), the API's logs also go there. `/health/live` and `/health/ready` show the API's health. In Rider, pick the **`FlightRadar.Api: http`** run configuration. Then open http://localhost:5123/scalar to explore the API, or use the sample requests in `src/FlightRadar.Api/FlightRadar.Api.http`.

#### OpenSky credentials (optional)

OpenSky allows 400 requests a day anonymously, or 4,000 with a free account. To use an account, create an **API client** on your [OpenSky account page](https://opensky-network.org/my-opensky/account), then give the API its client ID and secret. They're secrets, so never put them in `appsettings*.json` or anywhere in the repo:

- **Running the API locally:** .NET user-secrets, stored in your user profile outside the repo. In Rider: right-click `FlightRadar.Api` → *Tools* → *User Secrets*, and add:

  ```json
  { "Providers:OpenSky:ClientId": "…", "Providers:OpenSky:ClientSecret": "…" }
  ```

- **Docker:** copy `.env.example` to `.env` (git-ignored) and fill in `OPENSKY_CLIENT_ID` and `OPENSKY_CLIENT_SECRET`, then `docker compose up -d`.

To use OpenSky as the main source instead of the fallback, set `Providers:Active` to `OpenSky` (`FLIGHT_PROVIDER=OpenSky` in `.env`). OpenSky has no aircraft type or registration, so those are blank with it.

#### Run the web app

With the API running:

```bash
cd web
npm install
npm run dev
```

Open http://localhost:5173. The Vite dev server forwards `/api` requests to the API on port 5123.

#### Run the tests

```bash
dotnet test           # backend (xUnit)
cd web && npm test    # frontend (Vitest)
```

The tests never call the real flight data API: HTTP traffic is faked, and endpoint tests run the whole app in memory.

#### After changing the API contract

The frontend's TypeScript types are generated from the API's OpenAPI document. With the API running:

```bash
cd web && npm run gen:api
```

#### After changing the app icon

Edit `web/public/logo.svg`, then regenerate every icon size:

```bash
cd web && npm run gen:icons
```

The service worker only runs in production builds (`npm run build`, or the Docker image), not in `npm run dev`.

## Project layout

```
src/FlightRadar.Api/      ASP.NET Core Web API: endpoints, configuration, data providers, caching (+ Dockerfile)
src/FlightRadar.Core/     domain model, geo maths (incl. cache-key snapping), provider interface (no web dependencies)
tests/FlightRadar.Tests/  xUnit unit and endpoint tests
web/                      React + TypeScript frontend: map, aircraft markers, flight list, PWA (+ Dockerfile, nginx config)
src/FlightRadar.HealthProbe/  tiny health-check program for the API's container (its minimal image has no curl)
compose.yaml              runs the API, web and Redis containers together (+ Seq log viewer, optional)
docs/redis-lab.md         hands-on Redis exercises against the Compose stack
```

## Tech stack

| Area | Choice |
|---|---|
| Backend | ASP.NET Core minimal APIs, .NET 10, C# |
| Tests | xUnit and `WebApplicationFactory` (backend), Vitest (frontend) |
| API docs | OpenAPI + Scalar |
| Data sources | adsb.lol (primary), OpenSky Network (fallback; OAuth2 client credentials) |
| Resilience and caching | Microsoft.Extensions.Http.Resilience (Polly), HybridCache, Redis (optional, official image) |
| Observability | Serilog (structured logs, JSON in containers), Seq (optional), ASP.NET Core health checks |
| Frontend | React, TypeScript, Vite, react-leaflet, TanStack Query, vite-plugin-pwa (Workbox) |
| Containers | Docker multi-stage builds (chiseled .NET runtime, unprivileged nginx), Docker Compose |

## Roadmap

- [x] **0. Setup:** solution layout, shared build settings
- [x] **1. Backend:** `/api/flights`, adsb.lol provider, tests, OpenAPI docs
- [x] **2. Map UI:** click to set the centre, radius slider, circle, aircraft markers and list, smooth movement
- [x] **3. Docker:** multi-stage images for the API and web (nginx), Compose
- [x] **4. Mobile:** responsive layout for phones, installable as a Progressive Web App
- [x] **5. Hardening:** retries and circuit breaker ✓, caching (memory, then Redis, with grid-snapped keys) ✓, structured logging ✓, health checks ✓, real client IPs behind the proxy ✓, rate limiting ✓, OpenSky as a second provider with automatic fallback ✓
- [ ] **6. Enrichment:** airline names and probable routes for each flight
- [ ] **7. Real-time:** background polling with SignalR push (optionally Redis geo search); saved locations in Postgres
- [ ] **8. CI/CD:** GitHub Actions, optional deployment to Azure Container Apps

## About this project

This is a learning project, built to practise corporate-style .NET backend development, React with TypeScript, Docker and CI/CD, with an AI coding assistant as a pair programmer. Commits are kept small and focused so the history reads as a step-by-step build.

## Data and attribution

- Flight data: [adsb.lol](https://adsb.lol), a community ADS-B network whose data is published under the [Open Database License (ODbL)](https://opendatacommons.org/licenses/odbl/).
- Fallback flight data: [The OpenSky Network](https://opensky-network.org), a research network whose data is offered for non-commercial use; see its terms of use on its website before using it in anything else, and for how it asks to be credited in publications.
- Airline names: [Virtual Radar Server standing data](https://github.com/vradarserver/standing-data) (CC0, public domain), bundled as `src/FlightRadar.Core/Enrichment/airlines.json`; refresh with `scripts/update-airlines.ps1`. Community-maintained, so a few names may be out of date.
- Map tiles: © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors.

Live positions can be delayed or incomplete, and not every aircraft broadcasts ADS-B. **Not for navigation or any safety-related use.**
