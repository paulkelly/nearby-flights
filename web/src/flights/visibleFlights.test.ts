import { describe, expect, it } from 'vitest'
import type { Flight } from '../api/flights'
import { distanceKm } from '../geo/geo'
import { visibleFlights } from './visibleFlights'

// Due north of the centre, 0.1° of latitude ≈ 11.12 km.
const centre = { lat: 53.42, lon: -6.27 }

function flightAt(hex: string, lat: number, lon = -6.27, serverDistanceKm = 999): Flight {
  return {
    hex,
    callsign: null,
    airline: null,
    route: null,
    registration: null,
    aircraftType: null,
    lat,
    lon,
    positionTimestamp: '2026-11-04T12:00:00Z',
    distanceKm: serverDistanceKm, // what the server measured from *its* centre; must be ignored
    altitudeFt: null,
    onGround: false,
    groundSpeedKt: null,
    trackDeg: null,
    verticalRateFpm: null,
  }
}

const reported = (flight: Flight) => ({ lat: flight.lat, lon: flight.lon })

const near = flightAt('near', 53.52) // ~11 km
const mid = flightAt('mid', 53.72) // ~33 km
const far = flightAt('far', 53.92) // ~56 km

describe('visibleFlights', () => {
  it('hides flights outside the radius', () => {
    const result = visibleFlights([near, far, mid], centre, 50, reported)

    expect(result.map((v) => v.flight.hex)).not.toContain('far')
  })

  it('sorts nearest first', () => {
    const result = visibleFlights([far, near, mid], centre, 100, reported)

    expect(result.map((v) => v.flight.hex)).toEqual(['near', 'mid', 'far'])
  })

  it('measures from the given centre, ignoring the server’s distance', () => {
    const result = visibleFlights([near], centre, 50, reported)

    expect(result[0].distanceKm).toBeCloseTo(distanceKm(centre, reported(near)), 9)
  })

  it('shows flights again when they come back into range', () => {
    const elsewhere = { lat: 40, lon: -74 } // New York: nothing from Dublin is in range

    expect(visibleFlights([near, mid], elsewhere, 50, reported)).toEqual([])
    expect(visibleFlights([near, mid], centre, 50, reported)).toHaveLength(2)
  })

  it('includes a flight exactly on the edge', () => {
    const exactRadius = distanceKm(centre, reported(mid))

    expect(visibleFlights([mid], centre, exactRadius, reported)).toHaveLength(1)
  })

  it('uses positionOf, not the reported position', () => {
    // Pretend dead reckoning has carried "near" 50 km further north, out of a 40 km circle.
    const movedNorth = (flight: Flight) => ({ lat: flight.lat + 0.45, lon: flight.lon })

    expect(visibleFlights([near], centre, 40, movedNorth)).toEqual([])
  })

  it('keeps the original Flight objects (markers rely on their identity)', () => {
    const [visible] = visibleFlights([near], centre, 50, reported)

    expect(visible.flight).toBe(near)
  })
})
