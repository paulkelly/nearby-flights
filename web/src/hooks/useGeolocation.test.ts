import { describe, expect, it } from 'vitest'
import { describeGeolocationError } from './useGeolocation'

describe('describeGeolocationError', () => {
  it.each([
    [1, /permission was denied/],
    [2, /isn’t available/],
    [3, /took too long/],
    [99, /Couldn’t get your location/],
  ])('explains error code %i', (code, expected) => {
    expect(describeGeolocationError(code)).toMatch(expected)
  })
})
