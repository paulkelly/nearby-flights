import { ApiError } from './flights'

/** If a 429 has no usable Retry-After, wait one normal poll interval before trying again. */
export const DEFAULT_RATE_LIMIT_BACKOFF_MS = 10_000

/** True for "429 Too Many Requests" from our API. */
export function isRateLimited(error: unknown): error is ApiError {
  return error instanceof ApiError && error.status === 429
}

/**
 * Whether TanStack Query should retry a failed fetch.
 *
 * - 4xx (including 429): no. A 400 means our request is wrong, and a 429 means "slow down": retrying straight away
 *   is exactly what the API asked us not to do. (429 is handled by pausing instead; see rateLimitBackoffMs.)
 * - Anything else (502, network errors): once. The API already retries adsb.lol, so a 502 means it has given up;
 *   one browser retry covers a dropped connection without multiplying the load.
 */
export function shouldRetry(failureCount: number, error: unknown): boolean {
  const isClientError = error instanceof ApiError && error.status >= 400 && error.status < 500
  return !isClientError && failureCount < 1
}

/** How long to pause fetching after this error: the API's Retry-After for a 429, otherwise `null` (don't pause). */
export function rateLimitBackoffMs(error: unknown): number | null {
  if (!isRateLimited(error)) {
    return null
  }
  return error.retryAfterMs ?? DEFAULT_RATE_LIMIT_BACKOFF_MS
}
