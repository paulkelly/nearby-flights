import { latLng, type LeafletMouseEvent } from 'leaflet'
import { useEffect, useRef } from 'react'
import { Circle, CircleMarker, MapContainer, TileLayer, useMap, useMapEvents } from 'react-leaflet'
import type { Flight, LatLon } from '../api/flights'
import { roundLatLon } from '../geo/geo'
import { AircraftMarker } from './AircraftMarker'
import { TrackingLine } from './TrackingLine'

/** A request to move the map so this search circle is in view. */
export interface ViewRequest {
  centre: LatLon
  radiusKm: number
}

interface FlightMapProps {
  centre: LatLon
  radiusKm: number
  /** Set to a new object to move the map; null/unchanged leaves the view alone. */
  viewRequest: ViewRequest | null
  flights: Flight[]
  /** The aircraft on the nearest-flight card, joined to the centre by a line; null for none. */
  trackedFlight: Flight | null
  /** When our API fetched `flights` (server clock) and when the browser received them (browser clock, ms). */
  fetchedAt: string
  receivedAtMs: number
  selectedHex: string | null
  hoveredHex: string | null
  onCentreChange: (centre: LatLon) => void
  onSelect: (hex: string) => void
  onDeselect: (hex: string) => void
  onHover: (hex: string) => void
  onUnhover: (hex: string) => void
}

const INITIAL_ZOOM = 9

export function FlightMap({
  centre,
  radiusKm,
  viewRequest,
  flights,
  trackedFlight,
  fetchedAt,
  receivedAtMs,
  selectedHex,
  hoveredHex,
  onCentreChange,
  onSelect,
  onDeselect,
  onHover,
  onUnhover,
}: FlightMapProps) {
  return (
    // MapContainer's center/zoom are *initial* values only: Leaflet owns the map's view after it is created,
    // and changing these props later does nothing. Moving the view from code needs the map instance (useMap).
    <MapContainer center={[centre.lat, centre.lon]} zoom={INITIAL_ZOOM} className="flight-map">
      <TileLayer
        // OpenStreetMap's tile policy requires this attribution to be visible.
        attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
        url="https://tile.openstreetmap.org/{z}/{x}/{y}.png"
      />
      <SetCentreOnClickOrLongPress onCentreChange={onCentreChange} />
      <FlyToOnRequest request={viewRequest} />
      <InvalidateSizeOnResize />

      {/* Leaflet circle radii are in metres. */}
      <Circle center={[centre.lat, centre.lon]} radius={radiusKm * 1000} pathOptions={{ weight: 2, fillOpacity: 0.05 }} />
      {trackedFlight && (
        // key = hex: a different aircraft gets a fresh line (and animation loop) rather than one that jumps.
        <TrackingLine
          key={trackedFlight.hex}
          centre={centre}
          flight={trackedFlight}
          fetchedAt={fetchedAt}
          receivedAtMs={receivedAtMs}
        />
      )}
      {/* After the line, so the centre dot is drawn on top of the line's end. */}
      <CircleMarker center={[centre.lat, centre.lon]} radius={4} pathOptions={{ weight: 2, fillOpacity: 1 }} />

      {flights.map((flight) => (
        // key = hex: on each poll React matches markers to aircraft by hex, so an existing marker is *moved*
        // (and an open popup stays open) rather than destroyed and recreated.
        <AircraftMarker
          key={flight.hex}
          flight={flight}
          fetchedAt={fetchedAt}
          receivedAtMs={receivedAtMs}
          selected={flight.hex === selectedHex}
          hovered={flight.hex === hoveredHex}
          onSelect={onSelect}
          onDeselect={onDeselect}
          onHover={onHover}
          onUnhover={onUnhover}
        />
      ))}
    </MapContainer>
  )
}

/**
 * Leaflet measures its container once and only re-measures on *window* resize. On phones the map's height changes
 * when the bottom sheet expands or collapses while the window stays the same size, and without a re-measure the
 * newly exposed area stays grey and clicks land in the wrong place. A ResizeObserver watches the container itself.
 */
function InvalidateSizeOnResize() {
  const map = useMap()
  useEffect(() => {
    const observer = new ResizeObserver(() => map.invalidateSize())
    observer.observe(map.getContainer())
    return () => observer.disconnect()
  }, [map])
  return null
}

/**
 * Moves the map to show the requested area. Changing `centre` alone never moves the map (a click sets the centre
 * where you're already looking), but "Use my location" can point anywhere, so App asks for a move explicitly.
 * Each request is a new object, so the effect runs even when asked for the same place twice (e.g. after you
 * panned away).
 */
function FlyToOnRequest({ request }: { request: ViewRequest | null }) {
  const map = useMap()
  useEffect(() => {
    if (!request) return
    // Zoom so the whole search circle fits: a box twice the radius wide, centred on the position.
    const bounds = latLng(request.centre.lat, request.centre.lon).toBounds(request.radiusKm * 2 * 1000)
    map.flyToBounds(bounds, { padding: [16, 16], duration: 1 })
  }, [map, request])
  return null
}

/**
 * Renders nothing; exists to subscribe to map events. react-leaflet hooks only work inside <MapContainer>.
 *
 * Mouse: a click sets the centre. Touch: a *long-press* does, because on a phone you tap constantly to close popups
 * and select planes, and every one of those taps would otherwise start a new search somewhere.
 */
function SetCentreOnClickOrLongPress({ onCentreChange }: Pick<FlightMapProps, 'onCentreChange'>) {
  // A finger tap and a mouse click both arrive as `click`, so remember what kind of pointer touched the map last.
  const lastPointerType = useRef('mouse')

  const setCentreFrom = (event: LeafletMouseEvent) => {
    // wrap(): after panning sideways past the antimeridian Leaflet reports longitudes like 190 or -250,
    // which our API (rightly) rejects. wrap() maps them back into -180..180.
    const { lat, lng } = event.latlng.wrap()
    onCentreChange(roundLatLon({ lat, lon: lng }))
  }

  const map = useMapEvents({
    click(event) {
      if (lastPointerType.current !== 'touch') setCentreFrom(event)
    },
    // Browsers report a long-press on a touch screen as `contextmenu` (the same event as a mouse right-click).
    // Leaflet stops the browser's own menu from appearing because we handle the event.
    contextmenu(event) {
      navigator.vibrate?.(30) // short buzz so you can feel it registered (Android; ignored elsewhere)
      setCentreFrom(event)
    },
  })

  useEffect(() => {
    const container = map.getContainer()
    const remember = (event: PointerEvent) => {
      lastPointerType.current = event.pointerType // 'mouse', 'touch' or 'pen'
    }
    container.addEventListener('pointerdown', remember)
    return () => container.removeEventListener('pointerdown', remember)
  }, [map])

  return null
}
