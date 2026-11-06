import { describe, expect, it } from 'vitest'
import { ApiError } from './flights'
import { DEFAULT_RATE_LIMIT_BACKOFF_MS, isRateLimited, rateLimitBackoffMs, shouldRetry } from './retryPolicy'

const tooManyRequests = (retryAfterMs?: number) => new ApiError(429, 'Too many requests', retryAfterMs)

describe('shouldRetry', () => {
  it('never retries a 429: the API asked us to slow down', () => {
    expect(shouldRetry(0, tooManyRequests(2000))).toBe(false)
  })

  it('never retries a 400: the request itself is wrong', () => {
    expect(shouldRetry(0, new ApiError(400, 'Bad request'))).toBe(false)
  })

  it.each([
    ['a 502', new ApiError(502, 'Flight data unavailable')],
    ['a network error', new TypeError('Failed to fetch')],
  ])('retries %s once, then gives up', (_, error) => {
    expect(shouldRetry(0, error)).toBe(true)
    expect(shouldRetry(1, error)).toBe(false)
  })
})

describe('rateLimitBackoffMs', () => {
  it("uses the API's Retry-After for a 429", () => {
    expect(rateLimitBackoffMs(tooManyRequests(2000))).toBe(2000)
  })

  it('falls back to a default when a 429 has no Retry-After', () => {
    expect(rateLimitBackoffMs(tooManyRequests())).toBe(DEFAULT_RATE_LIMIT_BACKOFF_MS)
  })

  it.each([null, new ApiError(502, 'Flight data unavailable'), new TypeError('Failed to fetch')])(
    'does not pause for %s',
    (error) => {
      expect(rateLimitBackoffMs(error)).toBeNull()
    },
  )
})

describe('isRateLimited', () => {
  it('recognises only a 429 from our API', () => {
    expect(isRateLimited(tooManyRequests())).toBe(true)
    expect(isRateLimited(new ApiError(400, 'Bad request'))).toBe(false)
    expect(isRateLimited(new Error('429'))).toBe(false)
  })
})
