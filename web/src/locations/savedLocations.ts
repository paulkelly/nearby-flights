import type { LatLon } from '../api/flights'
import type { SavedLocation } from '../api/locations'

/** The API's limit (SavedLocationRules.MaxPerOwner). Used only to say so before the API would; it decides. */
export const MAX_SAVED_LOCATIONS = 20

/** The API stores coordinates to 3 decimal places (SavedLocationRules.CoordinateDecimals). */
const STORED_DECIMALS_FACTOR = 1000

/**
 * Rounds the way the API does: halves away from zero (.NET's MidpointRounding.AwayFromZero). JavaScript's
 * Math.round rounds halves *up*, so on its own it would turn -6.2705 into -6.270 where the API stored -6.271.
 */
export function roundAsStored(degrees: number): number {
  return (Math.sign(degrees) * Math.round(Math.abs(degrees) * STORED_DECIMALS_FACTOR)) / STORED_DECIMALS_FACTOR
}

/**
 * Is the map showing this saved location right now: the same centre (as the API would store it) and radius? Then
 * its button is highlighted, and saving here again would only make a duplicate.
 */
export function isCurrentView(location: SavedLocation, centre: LatLon, radiusKm: number): boolean {
  return (
    location.lat === roundAsStored(centre.lat) &&
    location.lon === roundAsStored(centre.lon) &&
    location.radiusKm === radiusKm
  )
}
