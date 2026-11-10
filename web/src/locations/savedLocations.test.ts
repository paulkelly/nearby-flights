import { describe, expect, it } from 'vitest'
import type { SavedLocation } from '../api/locations'
import { findByName, isCurrentView, roundAsStored } from './savedLocations'

describe('roundAsStored', () => {
  it.each([
    [53.42134, 53.421],
    [53.4215, 53.422],
    [-6.2705, -6.271], // halves away from zero, like the API: Math.round alone would give -6.270
    [-6.2704, -6.27],
    [0, 0],
  ])('rounds %s to %s', (given, stored) => {
    expect(roundAsStored(given)).toBe(stored)
  })
})

describe('isCurrentView', () => {
  const home: SavedLocation = { id: '1', name: 'Home', lat: 53.421, lon: -6.271, radiusKm: 10 }

  it('is true for the saved centre and radius', () => {
    expect(isCurrentView(home, { lat: 53.421, lon: -6.271 }, 10)).toBe(true)
  })

  it('is true for a centre the API would have stored as this one', () => {
    expect(isCurrentView(home, { lat: 53.4213, lon: -6.2705 }, 10)).toBe(true)
  })

  it('is false for another radius', () => {
    expect(isCurrentView(home, { lat: 53.421, lon: -6.271 }, 25)).toBe(false)
  })

  it('is false about 100 m away', () => {
    expect(isCurrentView(home, { lat: 53.422, lon: -6.271 }, 10)).toBe(false)
  })
})

describe('findByName', () => {
  const saved: SavedLocation[] = [
    { id: '1', name: 'Home', lat: 53.421, lon: -6.271, radiusKm: 10 },
    { id: '2', name: 'Work', lat: 53.35, lon: -6.26, radiusKm: 5 },
  ]

  it('finds a location by its name, ignoring surrounding spaces like the API', () => {
    expect(findByName(saved, '  Work ')?.id).toBe('2')
  })

  it('is case-sensitive, like the API: "home" is a different name', () => {
    expect(findByName(saved, 'home')).toBeUndefined()
  })

  it('finds nothing for a new name', () => {
    expect(findByName(saved, 'Gran’s')).toBeUndefined()
  })
})
