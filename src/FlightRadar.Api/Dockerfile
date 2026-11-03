# Build context is the repo root (see compose.yaml), so paths below are relative to it.

# Runtime image: ASP.NET Core runtime only, no SDK. Smaller and with less attack surface.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Copy only the project files first and restore. Docker caches this layer, so packages are
# only re-downloaded when a .csproj changes, not on every source edit.
COPY ["Directory.Build.props", "./"]
COPY ["src/FlightRadar.Api/FlightRadar.Api.csproj", "src/FlightRadar.Api/"]
COPY ["src/FlightRadar.Core/FlightRadar.Core.csproj", "src/FlightRadar.Core/"]
RUN dotnet restore "src/FlightRadar.Api/FlightRadar.Api.csproj"
COPY . .
RUN dotnet build "src/FlightRadar.Api/FlightRadar.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "src/FlightRadar.Api/FlightRadar.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FlightRadar.Api.dll"]
