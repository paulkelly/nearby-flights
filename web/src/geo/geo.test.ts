import { describe, expect, it } from 'vitest'
import { destinationPoint } from './geo'

// 1 degree of latitude (or of longitude on the equator) with a 6371 km Earth radius.
const ONE_DEGREE_KM = 111.1949

// Vitest's describe/it read like xUnit classes and [Fact]s; toBeCloseTo(x, 4) is Assert.Equal with a tolerance
// (4 decimal places here, i.e. within 0.00005 degrees ≈ 5 m).
describe('destinationPoint', () => {
  it('returns the start point for zero distance', () => {
    const result = destinationPoint({ lat: 53.42, lon: -6.27 }, 123, 0)

    expect(result.lat).toBeCloseTo(53.42, 6)
    expect(result.lon).toBeCloseTo(-6.27, 6)
  })

  it.each([
    // [description, bearing, expected lat, expected lon], all starting at (0, 0) and travelling 1 degree
    ['north', 0, 1, 0],
    ['east', 90, 0, 1],
    ['south', 180, -1, 0],
    ['west', 270, 0, -1],
  ])('moves one degree %s along a great circle', (_direction, bearing, expectedLat, expectedLon) => {
    const result = destinationPoint({ lat: 0, lon: 0 }, bearing, ONE_DEGREE_KM)

    expect(result.lat).toBeCloseTo(expectedLat, 4)
    expect(result.lon).toBeCloseTo(expectedLon, 4)
  })

  it('wraps longitude when crossing the antimeridian', () => {
    // 0.5° east of 179.5°E is 180°, and another 0.5° is 179.5°W, not 180.5°.
    const result = destinationPoint({ lat: 0, lon: 179.5 }, 90, ONE_DEGREE_KM)

    expect(result.lat).toBeCloseTo(0, 4)
    expect(result.lon).toBeCloseTo(-179.5, 4)
  })

  it('goes over the pole and comes down the other side', () => {
    // Heading north from 89°N, 0°E for 2 degrees: 1 degree up to the pole, 1 degree down the 180° meridian.
    const result = destinationPoint({ lat: 89, lon: 0 }, 0, 2 * ONE_DEGREE_KM)

    expect(result.lat).toBeCloseTo(89, 4)
    expect(Math.abs(result.lon)).toBeCloseTo(180, 4)
  })

  it('treats bearings outside 0-360 the same as their equivalents', () => {
    const start = { lat: 53.42, lon: -6.27 }

    const wrapped = destinationPoint(start, -90, 50)
    const normal = destinationPoint(start, 270, 50)

    expect(wrapped.lat).toBeCloseTo(normal.lat, 9)
    expect(wrapped.lon).toBeCloseTo(normal.lon, 9)
  })

  it('heads along a great circle, not a constant compass line', () => {
    // Due east from 60°N, a great circle curves towards the equator; a rhumb line would stay at 60°.
    const result = destinationPoint({ lat: 60, lon: 0 }, 90, 1000)

    expect(result.lat).toBeLessThan(60)
  })
})
