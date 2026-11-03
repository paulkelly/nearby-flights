# CLAUDE.md

Guidance for AI coding assistants working in this repository.

## Project

**Nearby Flights (FlightRadar)**: the user picks a location on a map and a radius, and the app shows live flight information for every aircraft inside that circle.

This is a **learning project**. The developer has 10 years of C# game-development experience (Rider) and is using this project to learn:
- AI-assisted development
- Corporate-style .NET backend practices
- React + TypeScript, Docker and CI/CD

Because of that, **explain the "why"** behind any non-obvious choice, especially for React, Docker, networking and ASP.NET Core patterns. Point out where a game-dev concept maps onto this domain (e.g. dead reckoning for smoothing aircraft positions between updates).

## Working agreement

- **Propose a plan before writing code** for anything bigger than a small fix, and wait for agreement.
- Keep changes small and focused, one concern per change, so they are easy to review.
- Don't add NuGet/npm packages without saying why and checking first.
- If a task would be a good learning exercise for the developer to write themselves (e.g. geo maths), say so instead of just doing it.
- Never commit secrets. API credentials go in user-secrets locally and environment variables / `.env` in Docker (`.env` is git-ignored).

## Tech stack

| Area | Choice |
|---|---|
| Backend | ASP.NET Core Web API, .NET 10, C# (nullable enabled) |
| Tests | xUnit |
| Frontend | React + TypeScript + Vite, `react-leaflet` with OpenStreetMap tiles, TanStack Query |
| Containers | Docker (multi-stage Dockerfiles), Docker Compose |
| IDE | JetBrains Rider (Windows) |

## Architecture

```
React (web/)  ──HTTP──►  FlightRadar.Api  ──►  IFlightDataProvider  ──►  external flight API
```

- The **browser never calls the flight data API directly**. The backend owns credentials, caching, provider switching and data shaping.
- External API responses are mapped to our own DTOs (e.g. `FlightDto`). Never leak third-party shapes to the frontend.
- Providers sit behind `IFlightDataProvider` so they can be swapped via configuration.

### Data providers
1. **adsb.lol** (first). No API key. `GET https://api.adsb.lol/v2/point/{lat}/{lon}/{radius}`. **Radius is in nautical miles, max 250.**
2. **OpenSky Network** (later). OAuth2 client-credentials auth (basic auth no longer supported). Filters by **bounding box only**, so compute a box around the circle, then filter by haversine distance. Requests cost credits by box size; non-commercial use only.

ADS-B data gives position, altitude, speed, heading, callsign and usually aircraft type, **not** origin/destination. Routes would need a separate lookup.

### API contract (initial)
`GET /api/flights?lat={lat}&lon={lon}&radiusKm={km}`. The public API uses **kilometres**; convert to provider units inside the provider.

## Target repository layout

```
FlightRadar.sln
src/FlightRadar.Api/        ASP.NET Core Web API (endpoints, DI, config)
src/FlightRadar.Core/       domain models, geo maths, provider interfaces (no ASP.NET dependencies)
tests/FlightRadar.Tests/    xUnit tests
web/                        React + Vite + TypeScript frontend
compose.yaml                runs api + web (and later redis/postgres)
CLAUDE.md
```

**Current state:** the layout above is in place except `web/`, which comes with step 2. Shared build settings live in `Directory.Build.props`. The API's Dockerfile is `src/FlightRadar.Api/Dockerfile`, built from the repo root by `compose.yaml`.

Backend notes worth knowing before changing things:
- `FlightRadar.Core/Flights/NearbyFlightsService` applies the exact radius and nearest-first sort; providers only promise "roughly this area".
- Providers report upstream failures as `FlightDataUnavailableException`, which `FlightDataUnavailableExceptionHandler` maps to 502 ProblemDetails.
- adsb.lol wire types are `internal` to `Providers/AdsbLol/`; `alt_baro` is a number or `"ground"` and is pressure altitude (can be negative on high-pressure days).
- JSON number handling is `Strict` so OpenAPI documents numbers as `number`, not `number | string`.
- OpenAPI (`/openapi/v1.json`) and Scalar (`/scalar`) are Development-only. `src/FlightRadar.Api/FlightRadar.Api.http` has sample requests.

## Conventions

### C#
- File-scoped namespaces, `record` types for DTOs, `sealed` classes by default.
- Async all the way; pass `CancellationToken` through to `HttpClient` calls.
- Typed `HttpClient` via `IHttpClientFactory`; configuration via the Options pattern (`IOptions<T>`).
- Keep `FlightRadar.Core` free of ASP.NET / infrastructure dependencies.
- Errors returned as ProblemDetails.

### Tests
- xUnit; name tests `Method_Scenario_ExpectedResult`.
- No real network calls in unit tests. Fake the `HttpMessageHandler`.
- Pure logic (unit conversion, haversine, bounding box) must have tests.

### Frontend
- TypeScript strict mode, function components and hooks.
- Server state via TanStack Query (poll ~10–15 s until SignalR is added).

## Common commands

```bash
dotnet build
dotnet test
dotnet run --project src/FlightRadar.Api
docker compose up --build
# frontend (once web/ exists)
cd web && npm install && npm run dev
```

## Roadmap

0. Setup & repo layout ✅
1. Backend skeleton: `/api/flights` + adsb.lol provider + tests + OpenAPI docs ✅
2. React map UI: click to set centre, radius slider, circle, aircraft markers + list ← **current**
3. Docker: multi-stage images for API and web (nginx), Compose
4. Hardening: resilience, caching (memory then Redis), Serilog, health checks, rate limiting, OpenSky provider with OAuth2
5. Real-time: background polling + SignalR; Postgres/EF Core for saved locations
6. CI/CD: GitHub Actions; optional deploy to Azure Container Apps
