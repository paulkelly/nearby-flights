// Display formatting for flight data. Aviation units are kept (feet, knots) because that's what pilots,
// controllers and every flight tracker use; only distance is metric, matching the radius the user picked.
import type { Flight } from './api/flights'

const numberFormat = new Intl.NumberFormat() // thousands separators in the user's locale, e.g. 35,000

export function formatAltitude(flight: Flight): string {
  if (flight.onGround) return 'On ground'
  if (flight.altitudeFt === null) return 'Unknown'
  return `${numberFormat.format(flight.altitudeFt)} ft`
}

export function formatSpeed(knots: number | null): string {
  return knots === null ? 'Unknown' : `${Math.round(knots)} kt`
}

export function formatTrack(degrees: number | null): string {
  return degrees === null ? 'Unknown' : `${Math.round(degrees)}°`
}

export function formatVerticalRate(feetPerMinute: number | null): string {
  if (feetPerMinute === null) return 'Unknown'
  if (Math.abs(feetPerMinute) < 100) return 'Level'
  const direction = feetPerMinute > 0 ? 'Climbing' : 'Descending'
  return `${direction} ${numberFormat.format(Math.abs(feetPerMinute))} ft/min`
}

export function formatDistance(km: number): string {
  return `${km.toFixed(1)} km`
}

/** Best human-readable name: callsign, else registration, else the ICAO hex code. */
export function displayName(flight: Flight): string {
  return flight.callsign ?? flight.registration ?? flight.hex.toUpperCase()
}
