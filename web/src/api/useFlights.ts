import { useState } from 'react'
import { keepPreviousData, useQuery, type UseQueryResult } from '@tanstack/react-query'
import { useNow } from '../hooks/useNow'
import { fetchFlights, type FlightsResponse, type LatLon } from './flights'
import { rateLimitBackoffMs, shouldRetry } from './retryPolicy'

const POLL_INTERVAL_MS = 10_000

/** The API response plus when the browser received it (browser clock, ms), used for dead reckoning. */
export type ReceivedFlights = FlightsResponse & { receivedAtMs: number }

/** The query cache key for a search: shared with useFlightStream, which writes pushed data under it. */
export const flightsQueryKey = (centre: LatLon, radiusKm: number) => ['flights', centre.lat, centre.lon, radiusKm]

export type FlightsQuery = UseQueryResult<ReceivedFlights> & {
  /** The API answered 429 and its Retry-After hasn't passed yet: fetching is paused. */
  isRateLimited: boolean
}

/**
 * Live aircraft around `centre`, refreshed every 10 seconds.
 *
 * TanStack Query owns the fetching: it caches results under `queryKey`, re-fetches on an interval, retries
 * failures, and exposes loading/error flags. Components just read `data`, `error`, `isFetching`.
 *
 * Plus `isRateLimited`: after a 429 the hook stops fetching until the API's `Retry-After` has passed. That covers
 * every area, not just the one that got the 429, because the API's limit is per client: moving the map while
 * limited would only collect more 429s.
 *
 * `streamed`: live pushes (useFlightStream) are keeping this search's data fresh, so polling stops. The query stays
 * the one place the data lives either way: pushes are written into its cache entry.
 *
 * `active`: false while the app hasn't decided where to look yet (useStartingPoint), so nothing is fetched for a
 * place nobody asked about.
 */
export function useFlights(centre: LatLon, radiusKm: number, streamed = false, active = true): FlightsQuery {
  // When the pause ends (browser clock, ms). Set at the moment a 429 arrives, in queryFn below: that's the event
  // that causes the pause, so no effect is needed to react to it afterwards.
  const [pausedUntilMs, setPausedUntilMs] = useState(0)

  // Re-checked every second, so fetching resumes within a second of Retry-After passing.
  const now = useNow(1000)
  const isRateLimited = now < pausedUntilMs

  // Types stated explicitly: otherwise TanStack infers the error type from `retry`'s parameter (`unknown`).
  const query = useQuery<ReceivedFlights, Error>({
    // The key identifies the data. When centre or radius change, the key changes, so a new fetch starts
    // automatically, and the in-flight request for the old key is cancelled via `signal`.
    queryKey: flightsQueryKey(centre, radiusKm),
    // The receive time is stored *with* the data rather than read from the query's `dataUpdatedAt`: while
    // placeholder data from the previous area is on screen, `dataUpdatedAt` describes the new (still empty) key,
    // so it would pair the old aircraft with the wrong timestamp.
    queryFn: async ({ signal }): Promise<ReceivedFlights> => {
      try {
        return { ...(await fetchFlights(centre, radiusKm, signal)), receivedAtMs: Date.now() }
      } catch (error) {
        const backoffMs = rateLimitBackoffMs(error)
        if (backoffMs !== null) {
          setPausedUntilMs(Date.now() + backoffMs)
        }
        throw error
      }
    },

    // Polling pauses while the browser tab is hidden (refetchIntervalInBackground defaults to false),
    // which spares adsb.lol requests nobody will see.
    // Not while pushes are arriving: the server sends each update as soon as it has it.
    refetchInterval: streamed ? false : POLL_INTERVAL_MS,

    // While the new area loads, keep showing the previous aircraft instead of an empty map.
    placeholderData: keepPreviousData,

    // No retries for 4xx (including 429); one for 502s and network errors. See shouldRetry.
    retry: shouldRetry,

    // Switched off while rate-limited: no polling, and no fetch for a new area either. When it's switched back on,
    // the data is stale, so TanStack fetches straight away.
    // Also off while streamed, even for a new search: the reply to Watch brings its first data. If Watch fails or
    // pushes stop, `streamed` turns false and the query fetches straight away (its data is stale by then).
    enabled: active && !isRateLimited && !streamed,
  })

  return { ...query, isRateLimited }
}
