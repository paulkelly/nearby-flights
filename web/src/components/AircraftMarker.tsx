import type { LatLngTuple, Marker as LeafletMarker } from 'leaflet'
import { useCallback, useEffect, useMemo, useRef } from 'react'
import { Marker, Popup } from 'react-leaflet'
import type { Flight, LatLon } from '../api/flights'
import { displayName } from '../format'
import { positionAgeMs } from '../geo/deadReckoning'
import { useNow } from '../hooks/useNow'
import { aircraftIcon } from './aircraftIcon'
import { Detail, FlightDetails, FlightIdentity } from './FlightInfo'
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

  // Selected or unselected from elsewhere (the list, the card)? Open or close this marker's popup to match.
  // Leaflet's popup autoPan scrolls the map if the aircraft is off-screen. Popups are imperative (method calls on
  // a Leaflet object), so this happens in an effect via a ref rather than in the JSX. Closing fires popupclose →
  // onDeselect, which does nothing by then: the selection has already moved on.
  useEffect(() => {
    const marker = markerRef.current
    if (selected) {
      marker?.openPopup()
    } else if (marker?.isPopupOpen()) {
      marker.closePopup()
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
        // A toggle, like Leaflet's own popup handling: clicking the selected aircraft unselects it. Leaflet closes
        // the popup on that second click (firing popupclose → onDeselect), and without this our handler would then
        // select it again, leaving it orange with no popup. `selected` is from the render the click happened in.
        click: () => (selected ? onDeselect(flight.hex) : onSelect(flight.hex)),
        popupclose: () => onDeselect(flight.hex),
        mouseover: () => onHover(flight.hex),
        mouseout: () => onUnhover(flight.hex),
      }}
    >
      {/* Popup content is React, so values are escaped automatically, unlike the icon's raw HTML.
          pane="popupPane": react-leaflet gives every child of a <Pane> that pane, popups included, so a grounded
          aircraft's popup would otherwise be drawn in the grounded aircraft's layer, under everything else. */}
      <Popup pane="popupPane" minWidth={260}>
        <PopupContent flight={flight} fetchedAt={fetchedAt} receivedAtMs={receivedAtMs} />
      </Popup>
    </Marker>
  )
}

/**
 * The popup's content: the same identity and details as the nearest card, plus the registration.
 *
 * react-leaflet only renders a popup's content while it's open, so this clock (for the dead-reckoned altitude)
 * ticks only for the one open popup, not for every aircraft on the map.
 */
function PopupContent({ flight, fetchedAt, receivedAtMs }: Pick<AircraftMarkerProps, 'flight' | 'fetchedAt' | 'receivedAtMs'>) {
  const now = useNow(1_000)
  const ageMs = positionAgeMs(flight.positionTimestamp, fetchedAt, receivedAtMs, now)

  return (
    <div className="aircraft-popup">
      <FlightIdentity flight={flight} logoSize={40} />
      <FlightDetails flight={flight} ageMs={ageMs}>
        {flight.registration && <Detail label="Registration" value={flight.registration} />}
      </FlightDetails>
    </div>
  )
}
