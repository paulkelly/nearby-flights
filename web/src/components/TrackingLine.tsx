import type { LatLngTuple, Polyline as LeafletPolyline } from 'leaflet'
import { useCallback, useMemo, useRef } from 'react'
import { Polyline } from 'react-leaflet'
import type { Flight, LatLon } from '../api/flights'
import { useDeadReckoning } from './useDeadReckoning'

interface TrackingLineProps {
  centre: LatLon
  flight: Flight
  fetchedAt: string
  receivedAtMs: number
}

/**
 * A faint dashed line from the centre to the aircraft on the nearest-flight card, so you can see which one it is.
 *
 * The aircraft's end follows the same dead-reckoned position as its marker, every frame, via the same hook; otherwise
 * the line would only move every 10 s while the plane glides away from it. Paths are drawn in Leaflet's overlay
 * pane, which sits below the marker pane, so the line passes under the aircraft icons. Not interactive, so clicks go
 * through to the map (and set the centre) as usual.
 */
export function TrackingLine({ centre, flight, fetchedAt, receivedAtMs }: TrackingLineProps) {
  const lineRef = useRef<LeafletPolyline>(null)

  // Memoised for the same reason as the marker's position: a new array makes react-leaflet reset the line to the
  // reported position, fighting the animation loop.
  const reported = useMemo<LatLngTuple[]>(
    () => [
      [centre.lat, centre.lon],
      [flight.lat, flight.lon],
    ],
    [centre.lat, centre.lon, flight.lat, flight.lon],
  )

  const moveEnd = useCallback(
    ({ lat, lon }: LatLon) =>
      lineRef.current?.setLatLngs([
        [centre.lat, centre.lon],
        [lat, lon],
      ]),
    [centre.lat, centre.lon],
  )
  useDeadReckoning(flight, fetchedAt, receivedAtMs, moveEnd)

  return (
    <Polyline
      ref={lineRef}
      positions={reported}
      interactive={false}
      pathOptions={{ color: '#1565c0', weight: 2, opacity: 0.6, dashArray: '4 6' }}
    />
  )
}
