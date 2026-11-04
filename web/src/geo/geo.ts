import type { LatLon } from '../api/flights'

/** Mean Earth radius, the same value the backend's haversine uses. */
export const EARTH_RADIUS_KM = 6371

const toRadians = (degrees: number) => (degrees * Math.PI) / 180
const toDegrees = (radians: number) => (radians * 180) / Math.PI

/**
 * Rounds to 4 decimal places (≈ 11 m). Plenty for picking a search centre by tap or GPS, and it keeps URLs and
 * query cache keys tidy: GPS fixes jitter in the 6th decimal, which would otherwise look like a "new" centre.
 */
export function roundLatLon({ lat, lon }: LatLon): LatLon {
  const round = (value: number) => Math.round(value * 10_000) / 10_000
  return { lat: round(lat), lon: round(lon) }
}

/**
 * Where you end up after travelling `distanceKm` from `start` with initial compass bearing `bearingDeg`
 * (clockwise from north) along a great circle: the inverse of the haversine distance.
 *
 * Over the few kilometres an aircraft covers between updates the great-circle path is almost straight, but using
 * the exact formula costs nothing and stays correct near the poles and the antimeridian.
 */
export function destinationPoint(start: LatLon, bearingDeg: number, distanceKm: number): LatLon {
  const lat1 = toRadians(start.lat)
  const lon1 = toRadians(start.lon)
  const bearing = toRadians(bearingDeg)
  const angularDistance = distanceKm / EARTH_RADIUS_KM // distance as an angle at the Earth's centre

  const sinLat2 =
    Math.sin(lat1) * Math.cos(angularDistance) +
    Math.cos(lat1) * Math.sin(angularDistance) * Math.cos(bearing)
  // Clamp guards asin against rounding pushing the value just past ±1 (→ NaN), as in the backend haversine.
  const lat2 = Math.asin(Math.min(1, Math.max(-1, sinLat2)))

  const lon2 =
    lon1 +
    Math.atan2(
      Math.sin(bearing) * Math.sin(angularDistance) * Math.cos(lat1),
      Math.cos(angularDistance) - Math.sin(lat1) * Math.sin(lat2),
    )

  return { lat: toDegrees(lat2), lon: normaliseLongitude(toDegrees(lon2)) }
}

/** Wraps any longitude into [-180, 180), e.g. 180.5 → -179.5. */
function normaliseLongitude(lon: number): number {
  return ((((lon + 180) % 360) + 360) % 360) - 180
}
