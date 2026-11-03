import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { ApiError, fetchFlights, type LatLon } from './flights'

const POLL_INTERVAL_MS = 10_000

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
    queryFn: ({ signal }) => fetchFlights(centre, radiusKm, signal),

    // Polling pauses while the browser tab is hidden (refetchIntervalInBackground defaults to false),
    // which spares adsb.lol requests nobody will see.
    refetchInterval: POLL_INTERVAL_MS,

    // While the new area loads, keep showing the previous aircraft instead of an empty map.
    placeholderData: keepPreviousData,

    // Retrying a 400 can't help (the input is wrong); retrying a 502 or a network error might.
    retry: (failureCount, error) =>
      !(error instanceof ApiError && error.status >= 400 && error.status < 500) && failureCount < 2,
  })
}
