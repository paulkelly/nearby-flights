# Multi-stage build for the API. Build context is the repo root (see compose.yaml), so paths are relative to it.
# Stage 1 uses the big SDK image to compile; stage 2 copies only the output into a small runtime image.
# The SDK, source code and intermediate files never reach the final image.

# ---- Build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy only the files that affect package restore first. Docker caches each step as a layer and re-runs a step
# only when its inputs change, so packages are re-downloaded only when a project file changes, not on every edit.
COPY ["Directory.Build.props", "./"]
COPY ["src/FlightRadar.Api/FlightRadar.Api.csproj", "src/FlightRadar.Api/"]
COPY ["src/FlightRadar.Core/FlightRadar.Core.csproj", "src/FlightRadar.Core/"]
COPY ["src/FlightRadar.HealthProbe/FlightRadar.HealthProbe.csproj", "src/FlightRadar.HealthProbe/"]
RUN dotnet restore "src/FlightRadar.Api/FlightRadar.Api.csproj" \
 && dotnet restore "src/FlightRadar.HealthProbe/FlightRadar.HealthProbe.csproj"

# Now the source. publish compiles too, so no separate `dotnet build` step (that would compile twice).
COPY src/ src/
RUN dotnet publish "src/FlightRadar.Api/FlightRadar.Api.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# The health probe (see HEALTHCHECK below), in its own folder so its files can't mix with the API's.
RUN dotnet publish "src/FlightRadar.HealthProbe/FlightRadar.HealthProbe.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/probe \
    --no-restore \
    /p:UseAppHost=false

# ---- Migrations ----
# The schema changes (Data/Migrations) compiled into one executable, an EF Core "migration bundle". The `migrate`
# service in compose.yaml runs it once before the API starts, then exits. Schema changes are their own deployment
# step: API instances (maybe several) don't race to change tables on startup, and in production the API could use a
# database login that can't change them at all. Built only when asked for (compose's `target: migrate`).
FROM build AS migrations-build
COPY ["dotnet-tools.json", "./"]
RUN dotnet tool restore
RUN dotnet ef migrations bundle \
    --project src/FlightRadar.Api/FlightRadar.Api.csproj \
    --configuration $BUILD_CONFIGURATION \
    --output /app/efbundle

# The same runtime image as the API (the bundle contains the API's assembly, which needs ASP.NET Core), so the
# layers are shared. It reads ConnectionStrings__Postgres from the environment (DesignTimeAppDbContextFactory).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS migrate
WORKDIR /app
COPY --from=migrations-build /app/efbundle .
ENTRYPOINT ["/app/efbundle"]

# ---- Runtime ----
# Last stage, so it's what a build without a target produces (the API image).
# "Chiseled" Ubuntu: only what .NET needs to run. No shell, no package manager, non-root by default.
# Roughly half the size of the standard image and far less for an attacker to work with.
# It has no ICU library, so .NET runs in globalization-invariant mode; fine for us (we format with
# InvariantCulture and do no culture-specific sorting or display formatting on the server).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /app/probe ./probe/

# .NET 8+ images listen on 8080: ports below 1024 need root, and these images run as a non-root user.
EXPOSE 8080

# Docker runs this inside the container every 10 s; exit code 0 = healthy. Usually "curl -f …", but this image has
# no curl (or shell), so it runs our own tiny probe with the dotnet runtime that's already here. It asks
# /health/ready: Healthy and Degraded (e.g. Redis down, still serving) pass; Unhealthy fails. "docker compose ps"
# shows the result, and other services can wait for it with depends_on: condition: service_healthy.
# (Plain Docker only reports "unhealthy"; it doesn't restart the container. Kubernetes and Azure Container Apps
# ignore HEALTHCHECK and call /health/live and /health/ready directly with their own probes.)
HEALTHCHECK --interval=10s --timeout=5s --start-period=15s --retries=3 \
    CMD ["dotnet", "/app/probe/FlightRadar.HealthProbe.dll"]
ENTRYPOINT ["dotnet", "FlightRadar.Api.dll"]
