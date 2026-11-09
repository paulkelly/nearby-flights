import { useMemo } from 'react'
import type { LatLon } from '../api/flights'
import type { ReceivedFlights } from '../api/useFlights'
import { positionAgeMs, predictPosition } from '../geo/deadReckoning'
import { visibleFlights, type VisibleFlight } from './visibleFlights'

/**
 * How often the visible set is re-checked against the aircraft's predicted positions. Pass `useNow` with this
 * interval as `now`; App shares that one clock with the nearest-flight card so the two update together.
 */
export const RECHECK_INTERVAL_MS = 1_000

const NONE: VisibleFlight[] = []

/**
 * The aircraft to show right now: those whose *predicted* position is inside the current circle, nearest first.
 *
 * Re-checked once a second, so planes leave (or enter) the circle about when they actually cross its edge,
 * rather than up to 10 s late at the next poll. Once a second is plenty: an airliner moves ~250 m in that time,
 * and re-checking every animation frame would re-render the list 60 times a second for no visible gain.
 */
export function useVisibleFlights(
  data: ReceivedFlights | undefined,
  centre: LatLon,
  radiusKm: number,
  now: number,
): VisibleFlight[] {
  return useMemo(() => {
    if (!data) return NONE
    return visibleFlights(data.flights, centre, radiusKm, (flight) =>
      predictPosition(flight, positionAgeMs(flight.positionTimestamp, data.fetchedAt, data.receivedAtMs, now)),
    )
  }, [data, centre, radiusKm, now])
}
