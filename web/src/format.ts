// Display formatting for flight data. Aviation units are kept (feet, knots) because that's what pilots,
// controllers and every flight tracker use; only distance is metric, matching the radius the user picked.
import type { Airport, Flight, Route } from './api/flights'
import { LEVEL_FLIGHT_FPM } from './geo/deadReckoning'

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
  if (Math.abs(feetPerMinute) < LEVEL_FLIGHT_FPM) return 'Level'
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

/**
 * Compact route for tight spaces, in the codes passengers know: "DUB → LAS", or "PMI → DOH → LHR" with a stop.
 * Airports without an IATA code show their ICAO code instead (see airportCode).
 */
export function formatRouteCodes(route: Route): string {
  return route.airports.map(airportCode).join(' → ')
}

/** Readable route: "Dublin (DUB) → Chicago (ORD)". Uses the airport's name if its city is unknown. */
export function formatRouteLong(route: Route): string {
  return route.airports.map((airport) => `${airport.city ?? airport.name} (${airportCode(airport)})`).join(' → ')
}

/** How sure we are of a route line: a route on record, a guess from what the aircraft is doing, or nothing. */
export type RouteLineSource = 'route' | 'guess' | 'unknown'

/**
 * What to say about where a flight is going, in order of preference: its route (if one fits), else a guess from
 * the aircraft itself ("From Dublin (DUB)", "To …" or "At …"), else "Unknown route".
 *
 * `long` for the card and popup ("Dublin (DUB) → Las Vegas (LAS)"); `short` for list rows ("DUB → LAS", "From DUB").
 */
export function routeLine(flight: Flight, length: 'long' | 'short'): { text: string; source: RouteLineSource } {
  if (flight.route) {
    return { text: length === 'long' ? formatRouteLong(flight.route) : formatRouteCodes(flight.route), source: 'route' }
  }
  if (flight.airportGuess) {
    const { kind, airport } = flight.airportGuess
    const place = length === 'long' ? `${airport.city ?? airport.name} (${airportCode(airport)})` : airportCode(airport)
    return { text: `${kind} ${place}`, source: 'guess' }
  }
  return { text: 'Unknown route', source: 'unknown' }
}

/** The second line of a list row (the airline is on the first): the short route line. */
export function flightSummary(flight: Flight): string {
  return routeLine(flight, 'short').text
}
