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
src/FlightRadar.HealthProbe/ tiny console app: the API image's Docker HEALTHCHECK (chiseled image has no curl)
tests/FlightRadar.Tests/    xUnit tests
web/                        React + Vite + TypeScript frontend
compose.yaml                runs api + web + redis (+ seq with --profile logs; later postgres)
docs/redis-lab.md           hands-on Redis exercises
CLAUDE.md
```

**Current state:** the layout above is complete. Shared build settings live in `Directory.Build.props`. `docker compose up --build` runs the whole app at http://localhost:8080.

Container notes worth knowing before changing things:
- Only `web` (nginx) publishes a port to the network (`${WEB_PORT:-8080}`); `api` is reachable only on the Compose network as `http://api:8080`. `redis` is published on `127.0.0.1:6379` only (no password, so never more widely) for redis-cli and Rider runs. Inside a container `localhost` is that container, so services address each other by service name.
- API image (`src/FlightRadar.Api/Dockerfile`, context = repo root): SDK build stage → `aspnet:10.0-noble-chiseled` runtime. Chiseled = no shell (`docker exec` can't run `sh`; use `docker logs`), non-root, no ICU (globalization-invariant mode). Its context is filtered by the allow-list `Dockerfile.dockerignore` next to it; add new folders there if the API build ever needs them. It also builds `FlightRadar.HealthProbe` into `/app/probe`, which `HEALTHCHECK` runs every 10 s (GET `/health/ready`; exit 0/1); `docker inspect --format "{{json .State.Health}}" flightradar-api-1` shows its output.
- Web image (`web/Dockerfile`, context = `web/`): `node:24-alpine` build stage (`npm ci`, `npm run build`) → `nginxinc/nginx-unprivileged:1.30-alpine` runtime on port 8080.
- `web/nginx.conf.template` is filled from env vars at container start (`API_URL`, default `http://api:8080`; `NGINX_LOCAL_RESOLVERS`). `proxy_pass` uses a variable plus `resolver` so nginx starts without the API and follows API restarts. Hashed `/assets/` are cached for a year; `index.html` is `no-cache`; text is gzipped.
- `redis` (official `redis:8.10-alpine`): 32 MB `maxmemory` with `allkeys-lru`, no persistence, `redis-cli ping` health check. `api` gets `ConnectionStrings__Redis=redis:6379` and `depends_on: redis: condition: service_healthy`; `web` waits for `api` to be `service_healthy` and has its own `wget` health check. Hands-on exercises: `docs/redis-lab.md`.
- `docker compose up --scale api=2` runs two API instances. Docker DNS returns both IPs for `api` and nginx (variable `proxy_pass` + `resolver`) spreads requests across them; Redis is the cache they share. Don't add `container_name` or host `ports` to `api`: both break scaling.
- `seq` (`datalust/seq:2026.1`) is in the `logs` profile: started only by `docker compose --profile logs up` and stopped only by `docker compose --profile logs down` (a plain `down` leaves it running and can't remove the network). UI on `127.0.0.1:5341`, logs kept in the `seq-data` volume (`down -v` deletes them).
- The Compose network is pinned to `172.30.0.0/24` and `api` trusts forwarded headers from exactly that range (`ForwardedHeaders__KnownNetworks__0`). Change both together, then `docker compose down` once to recreate the network. On Docker Desktop every client reaches nginx as the gateway (`172.30.0.1`), so locally all clients share one IP.
- Containers run in Production, so Scalar/OpenAPI are off. `docker run -e ASPNETCORE_ENVIRONMENT=Development ...` turns them on for a one-off look.

Backend notes worth knowing before changing things:
- `FlightRadar.Core/Flights/NearbyFlightsService` applies the exact radius and nearest-first sort; providers only promise "roughly this area".
- Providers report upstream failures as `FlightDataUnavailableException`, which `FlightDataUnavailableExceptionHandler` maps to 502 ProblemDetails.
- The public radius limit (`FlightsEndpoints.MaxRadiusKm`, 400 km) is a product choice, deliberately below adsb.lol's 250 nm (≈ 463 km) so cache snapping has room. Changing it: that constant, `MaxRadiusKmText` next to it (an OpenAPI test checks they match), `MAX_RADIUS_KM` in `web/src/components/RadiusControl.tsx`, then `npm run gen:api`.
- Provider wiring is a decorator: `AddAdsbLolFlightDataProvider()` registers `AdsbLolFlightDataProvider` under its own type, and `AddCachedFlightDataProvider<T>()` registers `Caching/CachingFlightDataProvider` (wrapping `T`) as `IFlightDataProvider`. Tests that swap `IFlightDataProvider` for a fake therefore bypass the cache too.
- The cache is `HybridCache`, with Redis as its L2 when `ConnectionStrings:Redis` is set (`Caching/RedisCacheServiceCollectionExtensions.cs`; tuned so a missing Redis never slows or fails requests). Requests are snapped first (`FlightRadar.Core/Geo/AreaSnapping`: centre to the nearest point of a 10 km 3D grid, radius enlarged by the worst-case shift and bucketed), and the key and the provider call use the snapped circle; snapping uses the provider's own `IFlightDataProvider.MaxRadiusKm`. Key = snapped centre + radius, TTL `FlightCache:TtlSeconds` (5). Concurrent misses for one key share a single upstream call; failures aren't cached. Hits return a deserialized copy, so callers can't mutate each other's results.
- The adsb.lol `HttpClient` uses `AddStandardResilienceHandler` (total timeout → retry → circuit breaker → per-attempt timeout), configured from `Providers:AdsbLol` (`TotalTimeoutSeconds`, `AttemptTimeoutSeconds`, `MaxRetries`). `HttpClient.Timeout` is deliberately infinite: it would wrap retries and override the pipeline. Polly's `ExecutionRejectedException` (timeout, open circuit) must map to `FlightDataUnavailableException`, or it surfaces as a 500. The frontend retries a failed request only once, since the API has already retried.
- Logging is Serilog (`Logging/LoggingExtensions.cs`), configured from the `Serilog` section: readable console in Development, compact JSON per line in Production (`appsettings.Production.json`). Seq is added only if `Seq:ServerUrl` is set: the `http` launch profile and the `api` container set it; appsettings don't, so tests never wait on a missing Seq. One request-summary event per request (502–504 → Warning, `/health` → Verbose). Don't log query strings or exact coordinates (users' locations); log the snapped cache key instead. Use `[LoggerMessage]` partial methods for new log events.
- `Program.cs` wraps startup in try/catch with `Log.Fatal` + rethrow: the rethrow matters, because `WebApplicationFactory` runs the host inside `Program` and tests must see the original exception.
- `app.UseForwardedHeaders()` is first in the pipeline; it trusts loopback plus `ForwardedHeaders:KnownNetworks` (CIDR, validated at startup), `ForwardLimit` 1.
- Health: `/health/live` (no checks) and `/health/ready` (checks tagged `ready`; JSON body). Redis's check is registered only when Redis is configured and fails as Degraded (still 200). adsb.lol is deliberately not checked (an upstream outage would fail every instance at once). nginx doesn't proxy `/health`.
- adsb.lol wire types are `internal` to `Providers/AdsbLol/`; `alt_baro` is a number or `"ground"` and is pressure altitude (can be negative on high-pressure days).
- JSON number handling is `Strict` so OpenAPI documents numbers as `number`, not `number | string`.
- OpenAPI (`/openapi/v1.json`) and Scalar (`/scalar`) are Development-only. `src/FlightRadar.Api/FlightRadar.Api.http` has sample requests.
- Run the API with the `http` launch profile (port 5123, Development). Started without a profile (e.g. a leftover Rider run configuration), Kestrel falls back to port 5000 and Production, and the frontend's proxy gets `ECONNREFUSED`.

Frontend notes worth knowing before changing things:
- Vite proxies `/api` to `http://localhost:5123`, so the browser stays same-origin and the API needs no CORS. Frontend code always calls relative `/api/...`; nginx plays the proxy role in the container.
- `src/api/schema.d.ts` is generated from the OpenAPI document; never edit it. Regenerate with `npm run gen:api` while the API runs. The script runs `openapi-typescript` via `npx` with its own TypeScript 5 because it doesn't support this project's TypeScript 7 yet; when it does, make it a normal dev dependency.
- Linting is oxlint (the Vite template's default), not ESLint. `erasableSyntaxOnly` is on, so no TypeScript-only runtime syntax (parameter properties, enums, namespaces).
- `App` owns `selectedHex` and `hoveredHex`; list and map both read and set them. Clearing uses functional updates (`current === hex ? null : current`) because Leaflet's close/mouseout events can arrive after the next selection/hover.
- Aircraft icons are `divIcon`s cached per heading/ground/selected, with rotation on the inner SVG (Leaflet owns the outer element's `transform`). Their HTML is raw: only put numbers in it, never API strings. Hover highlight toggles a CSS class instead of swapping icons.
- Dead reckoning: each airborne marker runs a `requestAnimationFrame` loop calling `setLatLng` directly, bypassing React. Position age is computed by `positionAgeMs` without comparing browser and server clocks; `receivedAtMs` is stored with the query data (not `dataUpdatedAt`, which is wrong while placeholder data shows). The marker's `position` prop is memoised so re-renders don't reset it.

Mobile / PWA notes worth knowing before changing things:
- Below 768px (`@media (max-width: 767px)` in `App.css`) the sidebar becomes a bottom sheet; `sheetExpanded` in `App` only matters there (desktop CSS ignores it). Heights use `100dvh`, padding uses `env(safe-area-inset-*)` (with `viewport-fit=cover`). Hover styles are inside `@media (hover: hover)` so taps don't leave sticky highlights. The map's `InvalidateSizeOnResize` re-measures Leaflet when its container (not the window) resizes.
- Setting the centre: mouse click, or long-press on touch (`contextmenu`). Touch taps are ignored by tracking the last `pointerdown`'s `pointerType`. Right-click on desktop also sets the centre.
- "Use my location" (`useGeolocation`) and the service worker need a **secure context**: HTTPS or `localhost`. Plain `http://<LAN IP>` can test layout only; use a Cloudflare quick tunnel for phone tests of install/geolocation. Moving the map from code goes through `viewRequest` (a new object per request) → `FlyToOnRequest`.
- Icons: edit `public/logo.svg`, run `npm run gen:icons` (`pwa-assets.config.ts`; maskable padding kept inside Android's 80% safe zone; Apple icon needs an opaque background). The manifest is the hand-written `public/manifest.webmanifest`; `vite-plugin-pwa` has `manifest: false`.
- Service worker (`vite-plugin-pwa`/Workbox, production builds only): precaches the app shell; `/api` and map tiles are never cached (live data; OSM tile policy). `registerType: 'prompt'` + `UpdatePrompt` shows a reload banner. nginx must keep `sw.js` and the manifest `no-cache`, and serves the manifest as `application/manifest+json` (not in nginx's built-in MIME list).
- `npm audit` reports 2 low / 8 moderate advisories in `workbox-build`'s build-time dependencies; accepted on 2026-11-04 because nothing in them ships to the browser. Recheck when `vite-plugin-pwa` updates; don't run `npm audit fix --force` (it downgrades the plugin).

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
- Pure logic (geo maths, dead reckoning, formatting) gets Vitest tests next to the code (`*.test.ts`).

## Common commands

```bash
dotnet build
dotnet test
dotnet run --project src/FlightRadar.Api   # uses the "http" launch profile: port 5123
# containers (from the repo root)
docker compose up --build    # whole app at http://localhost:8080 (WEB_PORT overrides the port)
docker compose down          # stop and remove containers + network
docker compose logs -f api   # follow one service's logs (JSON lines)
docker compose ps            # status incl. (healthy)
docker compose --profile logs up --build   # plus Seq log viewer at http://localhost:5341
docker compose --profile logs down         # a plain `down` won't stop Seq
# frontend (from web/)
npm install
npm run dev        # http://localhost:5173, proxies /api to the API
npm test           # Vitest
npm run lint       # oxlint
npm run build      # type-check + production build
npm run gen:api    # regenerate src/api/schema.d.ts (API must be running)
npm run gen:icons  # regenerate PWA icons from public/logo.svg
# phone testing over HTTPS (repo root stack running on 8080)
cloudflared tunnel --url http://localhost:8080
```

## Roadmap

0. Setup & repo layout ✅
1. Backend skeleton: `/api/flights` + adsb.lol provider + tests + OpenAPI docs ✅
2. React map UI: click to set centre, radius slider, circle, aircraft markers + list, dead reckoning ✅
3. Docker: multi-stage images for API and web (nginx), Compose ✅
4. Mobile: bottom-sheet layout on phones, long-press to set centre, "Use my location", PWA (manifest, icons, service worker, update prompt) ✅
5. Hardening (← **current**), in stages: 5a client-side distance filter (predicted positions) ✅ · 5b resilience + in-memory `HybridCache` ✅ · 5c Redis as optional L2 cache + grid-snapped keys ✅ · 5d Serilog (+ optional Seq), health checks, forwarded headers ✅ · 5e rate limiting · 5f OpenSky provider with OAuth2 and secrets
6. Enrichment: airline names from the callsign's ICAO prefix (lookup table), route lookup (e.g. adsb.lol's route API, verify its shape first) done in the backend and cached per callsign; show airline + route in place of registration/type. Treat routes as "probable": route databases can be stale or wrong.
7. Real-time: background polling + SignalR; Postgres/EF Core for saved locations. Option: the poller writes every aircraft into a Redis **geo set** (`GEOADD`) and searches use `GEOSEARCH … BYRADIUS` instead of calling the provider per request (Redis geo sets are sorted sets scored by geohash).
8. CI/CD: GitHub Actions; optional deploy to Azure Container Apps
