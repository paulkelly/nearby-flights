import type { Marker as LeafletMarker } from 'leaflet'
import { useEffect, useRef } from 'react'
import { Marker, Popup } from 'react-leaflet'
import type { Flight } from '../api/flights'
import { displayName, formatAltitude, formatSpeed, formatTrack, formatVerticalRate } from '../format'
import { aircraftIcon } from './aircraftIcon'

interface AircraftMarkerProps {
  flight: Flight
  selected: boolean
  hovered: boolean
  onSelect: (hex: string) => void
  onDeselect: (hex: string) => void
  onHover: (hex: string) => void
  onUnhover: (hex: string) => void
}

export function AircraftMarker({
  flight,
  selected,
  hovered,
  onSelect,
  onDeselect,
  onHover,
  onUnhover,
}: AircraftMarkerProps) {
  const markerRef = useRef<LeafletMarker>(null)
  const icon = aircraftIcon(flight.trackDeg, flight.onGround, selected)

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
      position={[flight.lat, flight.lon]}
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
