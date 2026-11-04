import type { Flight, LatLon } from '../api/flights'
import { distanceKm } from '../geo/geo'

/**
 * A flight inside the current search circle, with its distance from the *current* centre.
 *
 * The original Flight object is kept as-is (not copied with a new distance) on purpose: map markers depend on the
 * object's identity, and a fresh copy every second would restart each marker's animation loop for nothing.
 */
export interface VisibleFlight {
  flight: Flight
  distanceKm: number
}

/**
 * Which of `flights` are within `radiusKm` of `centre`, nearest first.
 *
 * Done in the browser (as well as on the server) so the display is right even when the data is stale: after
 * moving the centre while offline, the cached aircraft are re-measured from the new centre, those outside the
 * circle are hidden, and they reappear if they come back into range.
 *
 * `positionOf` says where each flight is now; the app passes its dead-reckoned position, tests pass anything.
 */
export function visibleFlights(
  flights: readonly Flight[],
  centre: LatLon,
  radiusKm: number,
  positionOf: (flight: Flight) => LatLon,
): VisibleFlight[] {
  return flights
    .map((flight) => ({ flight, distanceKm: distanceKm(centre, positionOf(flight)) }))
    .filter((visible) => visible.distanceKm <= radiusKm)
    .sort((a, b) => a.distanceKm - b.distanceKm)
}
