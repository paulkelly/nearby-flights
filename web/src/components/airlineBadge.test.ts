import { describe, expect, it } from 'vitest'
import { badgeHue, logoUrl } from './airlineBadge'

describe('badgeHue', () => {
  it('is the same for the same code', () => {
    expect(badgeHue('EAI')).toBe(badgeHue('EAI'))
  })

  it('is a hue between 0 and 359', () => {
    for (const code of ['AAA', 'EIN', 'RYR', 'ZZZ', 'EAI']) {
      expect(badgeHue(code)).toBeGreaterThanOrEqual(0)
      expect(badgeHue(code)).toBeLessThan(360)
    }
  })

  it('usually differs between airlines', () => {
    const hues = new Set(['EIN', 'RYR', 'EZY', 'BAW', 'DLH', 'AFR', 'KLM', 'UAE'].map(badgeHue))
    expect(hues.size).toBeGreaterThanOrEqual(6)
  })
})

describe('logoUrl', () => {
  it('points at our API', () => expect(logoUrl('EIN')).toBe('/api/airlines/EIN/logo'))
})
