import { describe, expect, it } from 'vitest'
import type { FlightsResponse } from './flights'
import { coversCircle, isNewer, reconnectDelayMs } from './flightStream'

const response = (overrides: Partial<FlightsResponse> = {}): FlightsResponse => ({
  lat: 53.47,
  lon: -6.21,
  radiusKm: 20,
  count: 0,
  fetchedAt: '2026-11-10T12:00:00Z',
  flights: [],
  ...overrides,
})

describe('coversCircle', () => {
  it('is true for a circle inside the snapped area', () => {
    // Dublin Airport at 10 km snaps to a 20 km area centred about 5 km away.
    expect(coversCircle(response(), { lat: 53.4213, lon: -6.2701 }, 10)).toBe(true)
  })

  it('is false when the circle pokes out of the area', () => {
    expect(coversCircle(response(), { lat: 53.4213, lon: -6.2701 }, 16)).toBe(false)
  })

  it('is false for a push about somewhere else', () => {
    // The previous area's last push, arriving after moving to London.
    expect(coversCircle(response(), { lat: 51.47, lon: -0.45 }, 10)).toBe(false)
  })

  it('is true for an exact match, despite rounding', () => {
    expect(coversCircle(response(), { lat: 53.47, lon: -6.21 }, 20)).toBe(true)
  })
})

describe('isNewer', () => {
  it('prefers the later fetch', () => {
    const older = response({ fetchedAt: '2026-11-10T12:00:00Z' })
    const newer = response({ fetchedAt: '2026-11-10T12:00:10Z' })

    expect(isNewer(newer, older)).toBe(true)
    expect(isNewer(older, newer)).toBe(false)
  })

  it('accepts anything when there is nothing yet', () => {
    expect(isNewer(response(), undefined)).toBe(true)
  })

  it('does not replace data with an equally old copy', () => {
    expect(isNewer(response(), response())).toBe(false)
  })

  it('treats an unreadable time as old', () => {
    expect(isNewer(response({ fetchedAt: 'not a date' }), response())).toBe(false)
  })
})

describe('reconnectDelayMs', () => {
  it('retries quickly at first, then every 30 s forever', () => {
    expect([0, 1, 2, 3, 4, 50].map(reconnectDelayMs)).toEqual([0, 2_000, 5_000, 10_000, 30_000, 30_000])
  })
})
