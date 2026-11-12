import { useEffect, useRef, useState } from 'react'
import { HttpTransportType, HubConnectionBuilder, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useDocumentVisible } from '../hooks/useDocumentVisible'
import { useNow } from '../hooks/useNow'
import type { FlightsResponse, LatLon } from './flights'
import {
  coversCircle,
  FLIGHTS_UPDATED,
  HUB_PATH,
  isNewer,
  reconnectDelayMs,
  STALE_AFTER_MS,
  WATCH_RETRY_MS,
} from './flightStream'
import { flightsQueryKey, type ReceivedFlights } from './useFlights'

export interface FlightStream {
  /**
   * Pushes are arriving for exactly this search: polling can stop. False while connecting, after a refused Watch,
   * when pushes have stopped coming (the watchdog), or when there's no connection at all.
   */
  streamed: boolean
}

/** What the server has been asked to watch for this browser, and when data for it last arrived. */
interface WatchState {
  key: string
  /** False after Watch failed: polling covers this search until a retry (or a push) succeeds. */
  ok: boolean
  lastDataAtMs: number
}

const searchKey = (centre: LatLon, radiusKm: number) => `${centre.lat}:${centre.lon}:${radiusKm}`

/**
 * Live flights over a WebSocket (SignalR), written into the same TanStack Query cache entry that HTTP polling uses
 * (`flightsQueryKey`). The rest of the app can't tell where its data came from; `useFlights` just stops polling
 * while `streamed` is true, and starts again by itself whenever it isn't.
 *
 * Lifetimes, like a game client's netcode: one connection for the whole page, reconnecting for as long as it takes;
 * one subscription (Watch) at a time, renewed whenever the search changes or the connection comes back; dropped
 * (Unwatch) while the tab is hidden, so the server stops fetching and pushing for nobody.
 *
 * `active`: false = watch nothing for now (the app hasn't decided where to look yet). The connection still opens,
 * so it's ready when the first search comes.
 */
export function useFlightStream(centre: LatLon, radiusKm: number, active = true): FlightStream {
  const queryClient = useQueryClient()
  const visible = useDocumentVisible()
  const now = useNow(1_000) // drives the watchdog
  const [connection, setConnection] = useState<HubConnection | null>(null) // set while connected
  const [watch, setWatch] = useState<WatchState | null>(null)
  const key = searchKey(centre, radiusKm)

  // The connection outlives searches, but a push must be checked against the *current* search. Effects and
  // callbacks read it from this ref (updated after each render) instead of capturing an old one.
  const search = useRef({ centre, radiusKm })
  useEffect(() => {
    search.current = { centre, radiusKm }
  })

  /** Data from a push or a Watch reply: keep it if it covers the search and is newer than what's there. */
  const receive = useRef((response: FlightsResponse) => {
    const current = search.current
    if (!coversCircle(response, current.centre, current.radiusKm)) return

    queryClient.setQueryData<ReceivedFlights>(flightsQueryKey(current.centre, current.radiusKm), (existing) =>
      isNewer(response, existing) ? { ...response, receivedAtMs: Date.now() } : existing,
    )
    const currentKey = searchKey(current.centre, current.radiusKm)
    setWatch((state) => (state?.key === currentKey ? { ...state, ok: true, lastDataAtMs: Date.now() } : state))
  })

  // ---- The connection: one per page, kept alive for as long as the page is open ----
  useEffect(() => {
    // "This effect has been cleaned up", for the callbacks that may still fire afterwards (React's lint rightly objects
    // to reassigning a plain variable once rendering is over; an AbortController is the standard flag for it).
    const disposed = new AbortController()
    let retryTimer: ReturnType<typeof setTimeout> | undefined

    const hub = new HubConnectionBuilder()
      // WebSockets only, no negotiate request: one request, so it works with several API instances behind nginx.
      .withUrl(HUB_PATH, { transport: HttpTransportType.WebSockets, skipNegotiation: true })
      // After a drop: retry quickly, then every 30 s for ever (SignalR's default gives up after four tries).
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (context) => reconnectDelayMs(context.previousRetryCount) })
      .configureLogging(import.meta.env.DEV ? LogLevel.Information : LogLevel.Error)
      .build()

    hub.on(FLIGHTS_UPDATED, (response: FlightsResponse) => receive.current(response))
    hub.onreconnecting(() => setConnection(null)) // polling takes over meanwhile
    hub.onreconnected(() => setConnection(hub)) // a new connection: the Watch effect subscribes again

    // The first connect isn't covered by automatic reconnect: retry it by hand, on the same schedule.
    const start = (attempt: number) => {
      hub.start().then(
        () => {
          if (!disposed.signal.aborted) setConnection(hub)
        },
        () => {
          if (!disposed.signal.aborted) retryTimer = setTimeout(() => start(attempt + 1), reconnectDelayMs(attempt + 1))
        },
      )
    }
    start(0)

    return () => {
      disposed.abort()
      clearTimeout(retryTimer)
      setConnection(null)
      void hub.stop()
    }
  }, [])

  // ---- The subscription: one search at a time ----
  const { lat, lon } = centre
  useEffect(() => {
    if (!connection) return

    if (!visible || !active) {
      // Hidden tab: the server stops working for us. Polling is paused by TanStack too (background tab).
      connection.invoke('Unwatch').catch(() => {})
      return
    }

    const cancelled = new AbortController()
    let retryTimer: ReturnType<typeof setTimeout> | undefined
    const watchNow = () => {
      // Optimistic: stop polling for this search now; the reply carries its first data.
      setWatch({ key, ok: true, lastDataAtMs: Date.now() })
      connection.invoke<FlightsResponse>('Watch', lat, lon, radiusKm).then(
        (response) => {
          if (!cancelled.signal.aborted) receive.current(response)
        },
        () => {
          // Throttled, no flight data yet, or the connection dropped mid-call: poll meanwhile, try again shortly.
          if (cancelled.signal.aborted) return
          setWatch({ key, ok: false, lastDataAtMs: 0 })
          retryTimer = setTimeout(watchNow, WATCH_RETRY_MS)
        },
      )
    }
    watchNow()

    return () => {
      cancelled.abort()
      clearTimeout(retryTimer)
    }
  }, [connection, visible, active, key, lat, lon, radiusKm])

  // ---- The watchdog: subscribed, but nothing arriving? Poll until pushes come back ----
  // (Not while hidden: nothing is expected then.)
  const fresh = watch !== null && watch.key === key && watch.ok && (!visible || now - watch.lastDataAtMs < STALE_AFTER_MS)

  return { streamed: connection !== null && fresh }
}
