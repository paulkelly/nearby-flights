import { describe, expect, it } from 'vitest'
import { parseRetryAfterMs } from './retryAfter'

const NOW = Date.parse('2026-11-06T12:00:00Z')

describe('parseRetryAfterMs', () => {
  it('reads a number of seconds', () => {
    expect(parseRetryAfterMs('2', NOW)).toBe(2000)
    expect(parseRetryAfterMs(' 30 ', NOW)).toBe(30_000)
  })

  it('reads an HTTP date as the time until then', () => {
    expect(parseRetryAfterMs('Fri, 06 Nov 2026 12:00:05 GMT', NOW)).toBe(5000)
  })

  it('treats a date in the past as no wait', () => {
    expect(parseRetryAfterMs('Fri, 06 Nov 2026 11:59:00 GMT', NOW)).toBe(0)
  })

  it.each([null, '', 'soon', '-5', '1.5'])('returns undefined for %j', (header) => {
    expect(parseRetryAfterMs(header, NOW)).toBeUndefined()
  })
})
