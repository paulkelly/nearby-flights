import type { LatLngTuple, Marker as LeafletMarker } from 'leaflet'
import { useCallback, useEffect, useMemo, useRef } from 'react'
import { Marker, Popup } from 'react-leaflet'
import type { Flight, LatLon } from '../api/flights'
import { displayName, formatAltitude, formatRouteLong, formatSpeed, formatTrack, formatVerticalRate } from '../format'
import { aircraftIcon } from './aircraftIcon'
import { useDeadReckoning } from './useDeadReckoning'

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

  // Dead reckoning: move the marker to its predicted position every frame (see useDeadReckoning).
  // An open popup follows the marker automatically.
  const moveMarker = useCallback(({ lat, lon }: LatLon) => markerRef.current?.setLatLng([lat, lon]), [])
  useDeadReckoning(flight, fetchedAt, receivedAtMs, moveMarker)

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
            {flight.airline && (
              <>
                <dt>Airline</dt>
                <dd>{flight.airline}</dd>
              </>
            )}
            {flight.route && (
              <>
                <dt>Route</dt>
                <dd>{formatRouteLong(flight.route)}</dd>
              </>
            )}
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
