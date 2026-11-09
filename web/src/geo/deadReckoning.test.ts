import { describe, expect, it } from 'vitest'
import {
  MAX_EXTRAPOLATION_MS,
  MIN_GROUND_EXTRAPOLATION_KT,
  positionAgeMs,
  predictAltitudeFt,
  predictPosition,
} from './deadReckoning'

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

  it('moves an aircraft rolling along a runway', () => {
    // A landing roll-out or take-off roll: on the ground, but fast and in a straight line.
    const rolling = { ...eastbound, onGround: true, groundSpeedKt: 120 }

    expect(predictPosition(rolling, 10_000).lon).toBeCloseTo(kmIn(10, 120) / ONE_DEGREE_KM, 5)
  })

  it('moves an aircraft on the ground at exactly the minimum speed', () => {
    const slowest = { ...eastbound, onGround: true, groundSpeedKt: MIN_GROUND_EXTRAPOLATION_KT }

    expect(predictPosition(slowest, 10_000).lon).toBeGreaterThan(0)
  })

  it.each([
    ['taxiing', { onGround: true, groundSpeedKt: MIN_GROUND_EXTRAPOLATION_KT - 1 }],
    ['parked', { onGround: true, groundSpeedKt: 0 }],
    ['on the ground with unknown speed', { onGround: true, groundSpeedKt: null }],
    ['with unknown speed', { groundSpeedKt: null }],
    ['with unknown track', { trackDeg: null }],
  ])('keeps an aircraft %s at its reported position', (_case, override) => {
    const result = predictPosition({ ...eastbound, lat: 53.42, lon: -6.27, ...override }, 10_000)

    expect(result).toEqual({ lat: 53.42, lon: -6.27 })
  })
})

describe('predictAltitudeFt', () => {
  const climbing = { altitudeFt: 3000, verticalRateFpm: 1200, onGround: false }

  it('adds vertical speed × elapsed time', () => {
    expect(predictAltitudeFt(climbing, 30_000)).toBe(3600) // 1,200 ft/min for half a minute
  })

  it('works descending too', () => {
    expect(predictAltitudeFt({ ...climbing, verticalRateFpm: -800 }, 15_000)).toBe(2800)
  })

  it('rounds to 10 ft', () => {
    expect(predictAltitudeFt(climbing, 1_000)).toBe(3020) // 3,020 exactly
    expect(predictAltitudeFt({ ...climbing, verticalRateFpm: 1000 }, 1_000)).toBe(3020) // 3,016.7
  })

  it('stops extrapolating after the maximum, like position', () => {
    expect(predictAltitudeFt(climbing, 10 * 60_000)).toBe(predictAltitudeFt(climbing, MAX_EXTRAPOLATION_MS))
  })

  it('treats negative elapsed time as zero', () => {
    expect(predictAltitudeFt(climbing, -5_000)).toBe(3000)
  })

  it('never predicts a descent below 0 ft', () => {
    expect(predictAltitudeFt({ altitudeFt: 200, verticalRateFpm: -1500, onGround: false }, 30_000)).toBe(0)
  })

  it('leaves an already negative pressure altitude where it is rather than going lower', () => {
    expect(predictAltitudeFt({ altitudeFt: -100, verticalRateFpm: -500, onGround: false }, 30_000)).toBe(-100)
  })

  it.each([
    ['level (small rates are noise)', { verticalRateFpm: 64 }],
    ['with unknown vertical speed', { verticalRateFpm: null }],
    ['on the ground', { onGround: true }],
  ])('keeps the reported altitude when %s', (_case, override) => {
    expect(predictAltitudeFt({ ...climbing, ...override }, 10_000)).toBe(3000)
  })

  it('stays unknown without an altitude', () => {
    expect(predictAltitudeFt({ ...climbing, altitudeFt: null }, 10_000)).toBeNull()
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
