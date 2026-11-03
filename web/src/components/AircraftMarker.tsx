import type { LatLngTuple, Marker as LeafletMarker } from 'leaflet'
import { useEffect, useLayoutEffect, useMemo, useRef } from 'react'
import { Marker, Popup } from 'react-leaflet'
import type { Flight } from '../api/flights'
import { displayName, formatAltitude, formatSpeed, formatTrack, formatVerticalRate } from '../format'
import { canExtrapolate, positionAgeMs, predictPosition } from '../geo/deadReckoning'
import { aircraftIcon } from './aircraftIcon'

interface AircraftMarkerProps {
  flight: Flight
  /** When our API fetched this data (server clock). */
  fetchedAt: string
  /** When the browser received it (browser clock, ms). */
  receivedAtMs: number
  selected: boolean
  hovered: boolean
  onSelect: (hex: string) => void
  onDeselect: (hex: string) => void
  onHover: (hex: string) => void
  onUnhover: (hex: string) => void
}

export function AircraftMarker({
  flight,
  fetchedAt,
  receivedAtMs,
  selected,
  hovered,
  onSelect,
  onDeselect,
  onHover,
  onUnhover,
}: AircraftMarkerProps) {
  const markerRef = useRef<LeafletMarker>(null)
  const icon = aircraftIcon(flight.trackDeg, flight.onGround, selected)

  // react-leaflet moves the marker whenever the `position` prop is a *different array*, even with equal numbers.
  // Without useMemo, every re-render (a hover anywhere, for example) would yank the marker back to its reported
  // position, fighting the animation loop below. Now it only changes when a poll brings new coordinates.
  const reportedPosition = useMemo<LatLngTuple>(() => [flight.lat, flight.lon], [flight.lat, flight.lon])

  // Dead reckoning, game-loop style: every animation frame, move the Leaflet marker straight to its predicted
  // position. This deliberately bypasses React: re-rendering ~100 components 60 times a second would be wasteful,
  // while setLatLng on a marker is cheap. React decides *which* aircraft exist; this loop decides where they're drawn.
  //
  // useLayoutEffect (not useEffect) so the first predicted position is applied before the browser paints.
  // Otherwise each poll would show one frame at the older reported position, a visible backwards twitch.
  useLayoutEffect(() => {
    const marker = markerRef.current
    if (!marker || !canExtrapolate(flight)) return

    let frameId = 0
    const tick = () => {
      const ageMs = positionAgeMs(flight.positionTimestamp, fetchedAt, receivedAtMs, Date.now())
      const { lat, lon } = predictPosition(flight, ageMs)
      marker.setLatLng([lat, lon]) // an open popup follows the marker automatically
      frameId = requestAnimationFrame(tick) // the browser calls tick once per frame; paused in background tabs
    }
    tick()

    // A new poll (new `flight` object) or unmount stops this loop; the next run starts from the fresh report.
    return () => cancelAnimationFrame(frameId)
  }, [flight, fetchedAt, receivedAtMs])

  // Hover highlight: toggle a class on the marker's existing element rather than swapping the icon. A new icon
  // makes Leaflet replace the element under the mouse, which can fire extra mouseout/mouseover events (flicker).
  // `icon` is a dependency because when it *does* change (new heading, selection) Leaflet builds a fresh element
  // without our class, so the effect re-applies it.
  useEffect(() => {
    markerRef.current?.getElement()?.classList.toggle('aircraft-icon--hovered', hovered)
  }, [hovered, icon])

  // Selected from elsewhere (the list)? Open this marker's popup. Leaflet's popup autoPan then scrolls the map
  // if the aircraft is off-screen. Opening a popup is imperative (a method call on a Leaflet object), so it
  // happens in an effect via a ref rather than in the JSX.
  useEffect(() => {
    if (selected) {
      markerRef.current?.openPopup()
    }
  }, [selected])

  return (
    <Marker
      ref={markerRef}
      position={reportedPosition}
      icon={icon}
      // Draw the selected plane, then a hovered one, above overlapping planes.
      zIndexOffset={selected ? 1000 : hovered ? 500 : 0}
      title={displayName(flight)} // native hover tooltip
      eventHandlers={{
        click: () => onSelect(flight.hex),
        popupclose: () => onDeselect(flight.hex),
        mouseover: () => onHover(flight.hex),
        mouseout: () => onUnhover(flight.hex),
      }}
    >
      {/* Popup content is React, so values are escaped automatically, unlike the icon's raw HTML. */}
      <Popup>
        <div className="aircraft-popup">
          <strong>{displayName(flight)}</strong>
          <dl>
            <dt>Type</dt>
            <dd>{flight.aircraftType ?? 'Unknown'}</dd>
            <dt>Registration</dt>
            <dd>{flight.registration ?? 'Unknown'}</dd>
            <dt>Altitude</dt>
            <dd>{formatAltitude(flight)}</dd>
            <dt>Vertical</dt>
            <dd>{formatVerticalRate(flight.verticalRateFpm)}</dd>
            <dt>Speed</dt>
            <dd>{formatSpeed(flight.groundSpeedKt)}</dd>
            <dt>Track</dt>
            <dd>{formatTrack(flight.trackDeg)}</dd>
          </dl>
        </div>
      </Popup>
    </Marker>
  )
}
