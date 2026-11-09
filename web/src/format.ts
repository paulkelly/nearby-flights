// Display formatting for flight data. Aviation units are kept (feet, knots) because that's what pilots,
// controllers and every flight tracker use; only distance is metric, matching the radius the user picked.
import type { Airport, Flight, Route } from './api/flights'

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

/** The code passengers know (IATA, e.g. "DUB"), else the ICAO code ("EIDW") for airports without one. */
export function airportCode(airport: Airport): string {
  return airport.iata ?? airport.icao
}

/** Compact route for tight spaces: "DUB → ORD", or "PMI → DOH → LHR" with a stop. */
export function formatRouteCodes(route: Route): string {
  return route.airports.map(airportCode).join(' → ')
}

/** Readable route: "Dublin (DUB) → Chicago (ORD)". Uses the airport's name if its city is unknown. */
export function formatRouteLong(route: Route): string {
  return route.airports.map((airport) => `${airport.city ?? airport.name} (${airportCode(airport)})`).join(' → ')
}

/**
 * The second line of a list row: what you'd want to know about a flight at a glance. Airline and route when we
 * have them ("Aer Lingus · DUB → ORD"); otherwise the aircraft type, which is what private aircraft are best
 * described by.
 */
export function flightSummary(flight: Flight): string {
  const parts = [flight.airline, flight.route && formatRouteCodes(flight.route)].filter(Boolean)
  return parts.length > 0 ? parts.join(' · ') : (flight.aircraftType ?? '')
}
