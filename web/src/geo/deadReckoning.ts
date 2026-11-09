// Dead reckoning: estimate where an aircraft is *now* from its last reported position, speed and track.
// The same technique as client-side prediction for remote players in a networked game: render the extrapolated
// position between server updates, then snap to the truth when the next update arrives.
import type { Flight, LatLon } from '../api/flights'
import { destinationPoint } from './geo'

const KM_PER_NAUTICAL_MILE = 1.852
const MS_PER_HOUR = 3_600_000

/**
 * Never extrapolate further than this. If updates stop (API down, laptop asleep), aircraft stop moving
 * instead of flying off along a straight line forever. Normal polling keeps ages well below this.
 */
export const MAX_EXTRAPOLATION_MS = 30_000

/**
 * Aircraft on the ground at or above this speed are extrapolated too. Above it they're on a runway (a landing
 * roll-out or a take-off roll), which is straight; below it they're taxiing, and taxiways turn constantly, so a
 * straight-line guess would drive them through buildings. Taxi speeds are mostly under 30 kt; a landing touches
 * down at 120-150 kt and is usually off the runway by about 30.
 */
export const MIN_GROUND_EXTRAPOLATION_KT = 30

type MotionFields = Pick<Flight, 'lat' | 'lon' | 'groundSpeedKt' | 'trackDeg' | 'onGround'>

/**
 * Whether we extrapolate this aircraft at all: we need its speed and track, and on the ground it must be moving at
 * runway speed. Everything else stays at its reported position.
 */
export function canExtrapolate(flight: MotionFields): boolean {
  if (flight.groundSpeedKt === null || flight.trackDeg === null) return false
  return !flight.onGround || flight.groundSpeedKt >= MIN_GROUND_EXTRAPOLATION_KT
}

/** Where the aircraft probably is `elapsedMs` after its reported position. */
export function predictPosition(flight: MotionFields, elapsedMs: number): LatLon {
  const reported = { lat: flight.lat, lon: flight.lon }
  if (!canExtrapolate(flight) || flight.groundSpeedKt === null || flight.trackDeg === null) {
    // (The repeated null checks are for TypeScript: it can't see inside canExtrapolate to narrow the types.)
    return reported
  }

  const clampedMs = Math.min(Math.max(elapsedMs, 0), MAX_EXTRAPOLATION_MS)
  const distanceKm = (flight.groundSpeedKt * KM_PER_NAUTICAL_MILE * clampedMs) / MS_PER_HOUR
  return destinationPoint(reported, flight.trackDeg, distanceKm)
}

/**
 * How old the reported position is at `nowMs`, without ever comparing clocks on different machines.
 *
 * `Date.now() - positionTimestamp` would compare the browser's clock with adsb.lol's, and a PC clock that is
 * 20 s out would shift every prediction by 20 s. Instead, split the age into two parts, each measured on one clock:
 *   - age when our API fetched it:   fetchedAt - positionTimestamp  (server clocks, both NTP-synced)
 *   - time since the browser got it: nowMs - receivedAtMs           (both from the browser's clock)
 */
export function positionAgeMs(
  positionTimestamp: string,
  fetchedAt: string,
  receivedAtMs: number,
  nowMs: number,
): number {
  const ageWhenFetchedMs = Date.parse(fetchedAt) - Date.parse(positionTimestamp)
  return ageWhenFetchedMs + (nowMs - receivedAtMs)
}
