import { describe, expect, it } from 'vitest'
import type { Flight } from './api/flights'
import type { AirportGuess, Route } from './api/flights'
import {
  displayName,
  flightSummary,
  formatAltitude,
  formatDistance,
  formatRouteCodes,
  formatRouteLong,
  formatSpeed,
  formatTrack,
  formatVerticalRate,
  routeLine,
} from './format'

const flight: Flight = {
  hex: '4cad7b',
  callsign: 'EIN58P',
  airline: 'Aer Lingus',
  airlineCode: 'EIN',
  route: null,
  airportGuess: null,
  registration: 'EI-NSB',
  aircraftType: 'A20N',
  lat: 53.45,
  lon: -6.5,
  positionTimestamp: '2026-11-03T12:00:00Z',
  distanceKm: 15.23,
  altitudeFt: 35000,
  onGround: false,
  groundSpeedKt: 450.4,
  trackDeg: 89.65,
  verticalRateFpm: -704,
}

// Thousands separators depend on the machine's locale, so build the expected text the same way.
const thousands = (n: number) => new Intl.NumberFormat().format(n)

describe('formatAltitude', () => {
  it('shows feet with thousands separators', () => {
    expect(formatAltitude(flight)).toBe(`${thousands(35000)} ft`)
  })

  it('says on ground instead of an altitude', () => {
    expect(formatAltitude({ ...flight, onGround: true, altitudeFt: null })).toBe('On ground')
  })

  it('says unknown when altitude is missing in the air', () => {
    expect(formatAltitude({ ...flight, altitudeFt: null })).toBe('Unknown')
  })
})

describe('formatVerticalRate', () => {
  it.each([
    [1200, `Climbing ${thousands(1200)} ft/min`],
    [-704, 'Descending 704 ft/min'],
    [64, 'Level'], // small rates are sensor noise around level flight
    [-99, 'Level'],
    [null, 'Unknown'],
  ])('formats %s', (rate, expected) => {
    expect(formatVerticalRate(rate)).toBe(expected)
  })
})

describe('simple formatters', () => {
  it('rounds speed to whole knots', () => expect(formatSpeed(450.4)).toBe('450 kt'))
  it('rounds track to whole degrees', () => expect(formatTrack(89.65)).toBe('90°'))
  it('shows distance to one decimal place', () => expect(formatDistance(15.23)).toBe('15.2 km'))
  it('says unknown for missing speed and track', () => {
    expect(formatSpeed(null)).toBe('Unknown')
    expect(formatTrack(null)).toBe('Unknown')
  })
})

describe('displayName', () => {
  it('prefers the callsign', () => expect(displayName(flight)).toBe('EIN58P'))

  it('falls back to the registration', () => {
    expect(displayName({ ...flight, callsign: null })).toBe('EI-NSB')
  })

  it('falls back to the upper-cased hex code', () => {
    expect(displayName({ ...flight, callsign: null, registration: null })).toBe('4CAD7B')
  })
})

const dublinToChicago: Route = {
  airports: [
    { icao: 'EIDW', iata: 'DUB', name: 'Dublin Airport', city: 'Dublin', countryCode: 'IE' },
    { icao: 'KORD', iata: 'ORD', name: "Chicago O'Hare International Airport", city: 'Chicago', countryCode: 'US' },
  ],
}

describe('formatRouteCodes', () => {
  it('joins the IATA codes', () => expect(formatRouteCodes(dublinToChicago)).toBe('DUB → ORD'))

  it('falls back to the ICAO code for airports without an IATA code', () => {
    const route: Route = { airports: [dublinToChicago.airports[0], { ...dublinToChicago.airports[1], iata: null }] }
    expect(formatRouteCodes(route)).toBe('DUB → KORD')
  })

  it('includes stops', () => {
    const withStop: Route = { airports: [dublinToChicago.airports[0], dublinToChicago.airports[1], dublinToChicago.airports[0]] }
    expect(formatRouteCodes(withStop)).toBe('DUB → ORD → DUB')
  })
})

describe('formatRouteLong', () => {
  it('names the cities with their codes', () => {
    expect(formatRouteLong(dublinToChicago)).toBe('Dublin (DUB) → Chicago (ORD)')
  })

  it("uses the airport's name when the city is unknown", () => {
    const route: Route = { airports: [{ ...dublinToChicago.airports[0], city: null }, dublinToChicago.airports[1]] }
    expect(formatRouteLong(route)).toBe('Dublin Airport (DUB) → Chicago (ORD)')
  })
})

const fromDublin: AirportGuess = { kind: 'From', airport: dublinToChicago.airports[0] }

describe('routeLine', () => {
  it('prefers the route', () => {
    const withBoth = { ...flight, route: dublinToChicago, airportGuess: fromDublin }
    expect(routeLine(withBoth, 'long')).toEqual({ text: 'Dublin (DUB) → Chicago (ORD)', source: 'route' })
    expect(routeLine(withBoth, 'short')).toEqual({ text: 'DUB → ORD', source: 'route' })
  })

  it.each([
    ['From', 'From Dublin (DUB)', 'From DUB'],
    ['To', 'To Dublin (DUB)', 'To DUB'],
    ['At', 'At Dublin (DUB)', 'At DUB'],
  ] as const)('without a route, describes a %s guess', (kind, long, short) => {
    const guessed = { ...flight, airportGuess: { ...fromDublin, kind } }
    expect(routeLine(guessed, 'long')).toEqual({ text: long, source: 'guess' })
    expect(routeLine(guessed, 'short')).toEqual({ text: short, source: 'guess' })
  })

  it("uses the airport's name when its city is unknown", () => {
    const guessed = { ...flight, airportGuess: { kind: 'To' as const, airport: { ...dublinToChicago.airports[0], city: null } } }
    expect(routeLine(guessed, 'long').text).toBe('To Dublin Airport (DUB)')
  })

  it('says so when there is neither', () => {
    expect(routeLine(flight, 'long')).toEqual({ text: 'Unknown route', source: 'unknown' })
    expect(routeLine(flight, 'short')).toEqual({ text: 'Unknown route', source: 'unknown' })
  })
})

describe('flightSummary', () => {
  it('is the short route line', () => {
    expect(flightSummary({ ...flight, route: dublinToChicago })).toBe('DUB → ORD')
    expect(flightSummary({ ...flight, airportGuess: fromDublin })).toBe('From DUB')
    expect(flightSummary(flight)).toBe('Unknown route')
  })
})
