import { distanceKm } from '../geo/geo'
import type { FlightsResponse, LatLon } from './flights'

/** Where the API's SignalR hub lives. Relative: Vite's dev proxy and nginx forward /hubs/ like /api/. */
export const HUB_PATH = '/hubs/flights'

/** The client-side method the server pushes to (FlightsHub.FlightsUpdatedMethod). */
export const FLIGHTS_UPDATED = 'flightsUpdated'

/**
 * No push for this long while subscribed means pushes have stopped (the API's Redis backplane is down, say, which
 * stops pushes but not connections): fall back to polling until they come back. 2.5 server ticks.
 */
export const STALE_AFTER_MS = 25_000

/** How long to wait before a failed Watch (throttled, or no flight data yet) is tried again. */
export const WATCH_RETRY_MS = 5_000

/**
 * Reconnect delays: quickly at first (a blip), then every 30 s for as long as it takes. SignalR's default gives
 * up after four tries, which would leave a phone that lost signal for a minute polling for good.
 */
export function reconnectDelayMs(previousRetryCount: number): number {
  const delays = [0, 2_000, 5_000, 10_000]
  return delays[previousRetryCount] ?? 30_000
}

/**
 * Does this response cover the whole circle being searched? Pushes describe the server's snapped area, which always
 * contains the circles that snap to it; a push for some *other* area (the one watched just before, still arriving
 * as the server switches groups) doesn't, and must not replace the current data.
 */
export function coversCircle(response: FlightsResponse, centre: LatLon, radiusKm: number): boolean {
  const TOLERANCE_KM = 0.01 // rounding in the coordinates
  return distanceKm({ lat: response.lat, lon: response.lon }, centre) + radiusKm <= response.radiusKm + TOLERANCE_KM
}

/**
 * Keep the newest data, whatever order it arrives in: a push can overtake the reply to Watch, and two API instances
 * can both fetch an area in one tick. Unparseable times count as old.
 */
export function isNewer(candidate: FlightsResponse, current: FlightsResponse | undefined): boolean {
  if (!current) return true
  const time = (response: FlightsResponse) => Date.parse(response.fetchedAt) || 0
  return time(candidate) > time(current)
}
