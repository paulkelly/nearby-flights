import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { ApiError, fetchFlights, type FlightsResponse, type LatLon } from './flights'

const POLL_INTERVAL_MS = 10_000

/** The API response plus when the browser received it (browser clock, ms), used for dead reckoning. */
export type ReceivedFlights = FlightsResponse & { receivedAtMs: number }

/**
 * Live aircraft around `centre`, refreshed every 10 seconds.
 *
 * TanStack Query owns the fetching: it caches results under `queryKey`, re-fetches on an interval, retries
 * failures, and exposes loading/error flags. Components just read `data`, `error`, `isFetching`.
 */
export function useFlights(centre: LatLon, radiusKm: number) {
  return useQuery({
    // The key identifies the data. When centre or radius change, the key changes, so a new fetch starts
    // automatically, and the in-flight request for the old key is cancelled via `signal`.
    queryKey: ['flights', centre.lat, centre.lon, radiusKm],
    // The receive time is stored *with* the data rather than read from the query's `dataUpdatedAt`: while
    // placeholder data from the previous area is on screen, `dataUpdatedAt` describes the new (still empty) key,
    // so it would pair the old aircraft with the wrong timestamp.
    queryFn: async ({ signal }): Promise<ReceivedFlights> => ({
      ...(await fetchFlights(centre, radiusKm, signal)),
      receivedAtMs: Date.now(),
    }),

    // Polling pauses while the browser tab is hidden (refetchIntervalInBackground defaults to false),
    // which spares adsb.lol requests nobody will see.
    refetchInterval: POLL_INTERVAL_MS,

    // While the new area loads, keep showing the previous aircraft instead of an empty map.
    placeholderData: keepPreviousData,

    // Retrying a 400 can't help (the input is wrong); retrying a 502 or a network error might, but only once:
    // the API already retries adsb.lol (and stops calling it while its circuit breaker is open), so a 502 means
    // it has given up. More browser retries would just multiply the load (3 browser tries × 3 API tries each).
    retry: (failureCount, error) =>
      !(error instanceof ApiError && error.status >= 400 && error.status < 500) && failureCount < 1,
  })
}
