import { describe, expect, it } from 'vitest'
import {
  MAX_RADIUS_KM,
  MIN_RADIUS_KM,
  SLIDER_POSITIONS,
  radiusKmAt,
  roundRadiusKm,
  sliderPositionFor,
  stepRadiusKm,
} from './radiusScale'

/** Every value the slider can show, walking up with the arrow-key step. */
function allTidyValues(): number[] {
  const values = [MIN_RADIUS_KM]
  while (values[values.length - 1] < MAX_RADIUS_KM) {
    values.push(stepRadiusKm(values[values.length - 1], 1))
  }
  return values
}

describe('radiusKmAt', () => {
  it('spans the whole range', () => {
    expect(radiusKmAt(0)).toBe(MIN_RADIUS_KM)
    expect(radiusKmAt(SLIDER_POSITIONS)).toBe(MAX_RADIUS_KM)
  })

  it('puts the geometric mean of the ends in the middle of the slider', () => {
    // The defining property of a logarithmic scale: halfway along, the radius is √(min × max), so the small
    // radii get half the travel. With 1–250 km that's about 16 km.
    const middle = Math.sqrt(MIN_RADIUS_KM * MAX_RADIUS_KM)
    expect(radiusKmAt(SLIDER_POSITIONS / 2)).toBe(roundRadiusKm(middle))
  })

  it('never decreases as the slider moves right', () => {
    for (let position = 1; position <= SLIDER_POSITIONS; position++) {
      expect(radiusKmAt(position)).toBeGreaterThanOrEqual(radiusKmAt(position - 1))
    }
  })

  it('only produces tidy values', () => {
    const tidy = new Set(allTidyValues())
    for (let position = 0; position <= SLIDER_POSITIONS; position++) {
      expect(tidy.has(radiusKmAt(position))).toBe(true)
    }
  })

  it('clamps positions outside the slider', () => {
    expect(radiusKmAt(-50)).toBe(MIN_RADIUS_KM)
    expect(radiusKmAt(SLIDER_POSITIONS + 50)).toBe(MAX_RADIUS_KM)
  })
})

describe('sliderPositionFor', () => {
  it('is the inverse of radiusKmAt for every tidy value', () => {
    // So the thumb sits exactly where that value is, and every tidy value can be reached by dragging.
    for (const km of allTidyValues()) {
      expect(radiusKmAt(sliderPositionFor(km))).toBe(km)
    }
  })

  it('clamps radii outside the range', () => {
    expect(sliderPositionFor(MIN_RADIUS_KM / 2)).toBe(0)
    expect(sliderPositionFor(MAX_RADIUS_KM * 2)).toBe(SLIDER_POSITIONS)
  })
})

describe('roundRadiusKm', () => {
  it.each([
    [7.4, 7],
    [19.4, 19],
    [19.6, 20],
    [22.4, 20],
    [47.6, 50],
    [97.6, 100],
    [104, 100],
    [MIN_RADIUS_KM / 2, MIN_RADIUS_KM], // clamped to the slider's range, wherever its ends are set
    [MAX_RADIUS_KM + 7, MAX_RADIUS_KM],
  ])('rounds %f km to %i km', (km, expected) => {
    expect(roundRadiusKm(km)).toBe(expected)
  })
})

describe('stepRadiusKm', () => {
  it.each([
    [1, 1, 2],
    [5, 1, 6],
    [19, 1, 20],
    [20, 1, 25],
    [95, 1, 100],
    [100, 1, 110],
    [20, -1, 19],
    [25, -1, 20],
    [100, -1, 95],
    [110, -1, 100],
  ] as const)('from %i km in direction %i goes to %i km', (km, direction, expected) => {
    expect(stepRadiusKm(km, direction)).toBe(expected)
  })

  it('stops at the ends', () => {
    expect(stepRadiusKm(MIN_RADIUS_KM, -1)).toBe(MIN_RADIUS_KM)
    expect(stepRadiusKm(MAX_RADIUS_KM, 1)).toBe(MAX_RADIUS_KM)
  })

  it('round-trips: up then down returns to the same value', () => {
    for (const km of allTidyValues().slice(0, -1)) {
      expect(stepRadiusKm(stepRadiusKm(km, 1), -1)).toBe(km)
    }
  })
})
