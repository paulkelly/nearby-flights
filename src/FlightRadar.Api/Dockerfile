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
RUN dotnet restore "src/FlightRadar.Api/FlightRadar.Api.csproj"

# Now the source. publish compiles too, so no separate `dotnet build` step (that would compile twice).
COPY src/ src/
RUN dotnet publish "src/FlightRadar.Api/FlightRadar.Api.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---- Runtime ----
# "Chiseled" Ubuntu: only what .NET needs to run. No shell, no package manager, non-root by default.
# Roughly half the size of the standard image and far less for an attacker to work with.
# It has no ICU library, so .NET runs in globalization-invariant mode; fine for us (we format with
# InvariantCulture and do no culture-specific sorting or display formatting on the server).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
COPY --from=build /app/publish .

# .NET 8+ images listen on 8080: ports below 1024 need root, and these images run as a non-root user.
EXPOSE 8080
ENTRYPOINT ["dotnet", "FlightRadar.Api.dll"]
