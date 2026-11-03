import { describe, expect, it } from 'vitest'
import { MAX_EXTRAPOLATION_MS, positionAgeMs, predictPosition } from './deadReckoning'

const ONE_DEGREE_KM = 111.1949

// Heading due east along the equator, so distance travelled maps straight onto degrees of longitude.
const eastbound = { lat: 0, lon: 0, groundSpeedKt: 450, trackDeg: 90, onGround: false }

// 450 kt = 833.4 km/h, so 10 s covers 2.315 km.
const kmIn = (seconds: number, knots = 450) => (knots * 1.852 * seconds) / 3600

describe('predictPosition', () => {
  it('moves along the track by speed × elapsed time', () => {
    const result = predictPosition(eastbound, 10_000)

    expect(result.lat).toBeCloseTo(0, 6)
    expect(result.lon).toBeCloseTo(kmIn(10) / ONE_DEGREE_KM, 5)
  })

  it('stays put with no elapsed time', () => {
    expect(predictPosition(eastbound, 0)).toEqual({ lat: 0, lon: 0 })
  })

  it('treats negative elapsed time as zero', () => {
    // Can happen briefly with clock jitter; an aircraft should never be drawn moving backwards.
    expect(predictPosition(eastbound, -5_000)).toEqual({ lat: 0, lon: 0 })
  })

  it('stops extrapolating after the maximum', () => {
    const atMax = predictPosition(eastbound, MAX_EXTRAPOLATION_MS)
    const wayLater = predictPosition(eastbound, 10 * 60_000)

    expect(wayLater).toEqual(atMax)
  })

  it.each([
    ['on the ground', { onGround: true }],
    ['with unknown speed', { groundSpeedKt: null }],
    ['with unknown track', { trackDeg: null }],
  ])('keeps an aircraft %s at its reported position', (_case, override) => {
    const result = predictPosition({ ...eastbound, lat: 53.42, lon: -6.27, ...override }, 10_000)

    expect(result).toEqual({ lat: 53.42, lon: -6.27 })
  })
})

describe('positionAgeMs', () => {
  it('adds the age at fetch time to the time since the browser received it', () => {
    const age = positionAgeMs(
      '2026-11-03T12:00:00.000Z', // position reported
      '2026-11-03T12:00:02.000Z', // our API fetched it 2 s later
      1_000_000, // browser received the response at this (browser-clock) time
      1_003_000, // and it is now 3 s later on the same clock
    )

    expect(age).toBe(5_000)
  })

  it('is unaffected by the browser clock being wildly wrong', () => {
    // Same scenario, but the browser's clock reads a completely different epoch: only differences matter.
    const age = positionAgeMs('2026-11-03T12:00:00.000Z', '2026-11-03T12:00:02.000Z', 42, 3_042)

    expect(age).toBe(5_000)
  })
})
