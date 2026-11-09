import { describe, expect, it } from 'vitest'
import type { Flight } from '../api/flights'
import { LANDED_GRACE_MS, SWITCH_MARGIN, chooseNearest, type NearestTracking } from './nearestFlight'
import type { VisibleFlight } from './visibleFlights'

function at(hex: string, distanceKm: number, onGround = false): VisibleFlight {
  const flight: Flight = {
    hex,
    callsign: null,
    airline: null,
    registration: null,
    aircraftType: null,
    lat: 53.42,
    lon: -6.27,
    positionTimestamp: '2026-11-09T12:00:00Z',
    distanceKm,
    altitudeFt: onGround ? null : 3000,
    onGround,
    groundSpeedKt: null,
    trackDeg: null,
    verticalRateFpm: null,
  }
  return { flight, distanceKm }
}

/** Nearest first, as visibleFlights returns them. */
const sorted = (...flights: VisibleFlight[]) => [...flights].sort((a, b) => a.distanceKm - b.distanceKm)

const T0 = 1_000_000
const airborne = (hex: string): NearestTracking => ({ hex, onGround: false, landedAtMs: null })

/** Runs several updates in a row, carrying the tracking forward like the hook does. Returns the last choice. */
function run(steps: { atMs: number; visible: VisibleFlight[] }[]) {
  let tracking: NearestTracking | null = null
  let choice = chooseNearest([], null, 0)
  for (const step of steps) {
    choice = chooseNearest(step.visible, tracking, step.atMs)
    tracking = choice.tracking
  }
  return choice
}

describe('chooseNearest', () => {
  it('shows nothing when there are no aircraft', () => {
    expect(chooseNearest([], airborne('gone'), T0)).toEqual({ nearest: null, tracking: null })
  })

  it('picks the nearest aircraft in the air, skipping closer ones on the ground', () => {
    const choice = chooseNearest(sorted(at('parked', 1, true), at('flying', 5)), null, T0)

    expect(choice.nearest?.flight.hex).toBe('flying')
    expect(choice.tracking).toEqual(airborne('flying'))
  })

  it('falls back to the nearest aircraft on the ground when none are flying', () => {
    const choice = chooseNearest(sorted(at('far-parked', 4, true), at('near-parked', 1, true)), null, T0)

    expect(choice.nearest?.flight.hex).toBe('near-parked')
    expect(choice.tracking).toEqual({ hex: 'near-parked', onGround: true, landedAtMs: null })
  })

  describe('hysteresis', () => {
    it('keeps the tracked aircraft when another is only slightly closer', () => {
      const slightlyCloser = 10 * (1 - SWITCH_MARGIN / 2)
      const choice = chooseNearest(sorted(at('tracked', 10), at('other', slightlyCloser)), airborne('tracked'), T0)

      expect(choice.nearest?.flight.hex).toBe('tracked')
    })

    it('switches when another is clearly closer', () => {
      const clearlyCloser = 10 * (1 - SWITCH_MARGIN * 2)
      const choice = chooseNearest(sorted(at('tracked', 10), at('other', clearlyCloser)), airborne('tracked'), T0)

      expect(choice.nearest?.flight.hex).toBe('other')
    })

    it('switches when the tracked aircraft leaves the circle', () => {
      const choice = chooseNearest(sorted(at('other', 30)), airborne('tracked'), T0)

      expect(choice.nearest?.flight.hex).toBe('other')
    })
  })

  describe('landing', () => {
    it('keeps a tracked aircraft that has just landed, even though others are flying', () => {
      const choice = run([
        { atMs: T0, visible: sorted(at('lander', 2), at('flying', 8)) },
        { atMs: T0 + 1_000, visible: sorted(at('lander', 2, true), at('flying', 8)) },
      ])

      expect(choice.nearest?.flight.hex).toBe('lander')
      expect(choice.tracking).toEqual({ hex: 'lander', onGround: true, landedAtMs: T0 + 1_000 })
    })

    it('still keeps it just before the grace period ends', () => {
      const landedAt = T0 + 1_000
      const choice = run([
        { atMs: T0, visible: sorted(at('lander', 2), at('flying', 8)) },
        { atMs: landedAt, visible: sorted(at('lander', 2, true), at('flying', 8)) },
        { atMs: landedAt + LANDED_GRACE_MS - 1, visible: sorted(at('lander', 2, true), at('flying', 8)) },
      ])

      expect(choice.nearest?.flight.hex).toBe('lander')
    })

    it('moves on to an aircraft in the air once the grace period is over', () => {
      // The grace period counts from the landing, not from each update: landedAtMs is carried forward.
      const landedAt = T0 + 1_000
      const choice = run([
        { atMs: T0, visible: sorted(at('lander', 2), at('flying', 8)) },
        { atMs: landedAt, visible: sorted(at('lander', 2, true), at('flying', 8)) },
        { atMs: landedAt + 10_000, visible: sorted(at('lander', 2, true), at('flying', 8)) },
        { atMs: landedAt + LANDED_GRACE_MS, visible: sorted(at('lander', 2, true), at('flying', 8)) },
      ])

      expect(choice.nearest?.flight.hex).toBe('flying')
    })

    it('keeps a landed aircraft after the grace period if nothing else is flying', () => {
      const choice = run([
        { atMs: T0, visible: sorted(at('lander', 2), at('parked', 5, true)) },
        { atMs: T0 + 1_000, visible: sorted(at('lander', 2, true), at('parked', 5, true)) },
        { atMs: T0 + 60_000, visible: sorted(at('lander', 2, true), at('parked', 5, true)) },
      ])

      expect(choice.nearest?.flight.hex).toBe('lander')
    })

    it('does not give other grounded aircraft a grace period', () => {
      // Only the tracked aircraft is kept after landing; a different plane that has just landed isn't picked.
      const choice = chooseNearest(sorted(at('other-lander', 1, true), at('flying', 8)), airborne('flying'), T0)

      expect(choice.nearest?.flight.hex).toBe('flying')
    })
  })

  describe('take-off', () => {
    it('moves from a grounded fallback to the first aircraft in the air, straight away', () => {
      // The parked aircraft was only shown because nothing was flying; it didn't land while tracked, so no grace.
      const choice = run([
        { atMs: T0, visible: sorted(at('parked', 1, true)) },
        { atMs: T0 + 1_000, visible: sorted(at('parked', 1, true), at('departing', 3)) },
      ])

      expect(choice.nearest?.flight.hex).toBe('departing')
    })

    it('clears the landing time when the tracked aircraft takes off', () => {
      const choice = run([
        { atMs: T0, visible: sorted(at('plane', 1, true)) },
        { atMs: T0 + 5_000, visible: sorted(at('plane', 1)) },
      ])

      expect(choice.tracking).toEqual(airborne('plane'))
    })
  })
})
