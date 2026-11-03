import { Circle, CircleMarker, MapContainer, TileLayer, useMapEvents } from 'react-leaflet'
import type { Flight, LatLon } from '../api/flights'
import { AircraftMarker } from './AircraftMarker'

interface FlightMapProps {
  centre: LatLon
  radiusKm: number
  flights: Flight[]
  selectedHex: string | null
  onCentreChange: (centre: LatLon) => void
  onSelect: (hex: string) => void
  onDeselect: (hex: string) => void
}

const INITIAL_ZOOM = 9

export function FlightMap({
  centre,
  radiusKm,
  flights,
  selectedHex,
  onCentreChange,
  onSelect,
  onDeselect,
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
      <ClickToSetCentre onCentreChange={onCentreChange} />

      {/* Leaflet circle radii are in metres. */}
      <Circle center={[centre.lat, centre.lon]} radius={radiusKm * 1000} pathOptions={{ weight: 2, fillOpacity: 0.05 }} />
      <CircleMarker center={[centre.lat, centre.lon]} radius={4} pathOptions={{ weight: 2, fillOpacity: 1 }} />

      {flights.map((flight) => (
        // key = hex: on each poll React matches markers to aircraft by hex, so an existing marker is *moved*
        // (and an open popup stays open) rather than destroyed and recreated.
        <AircraftMarker
          key={flight.hex}
          flight={flight}
          selected={flight.hex === selectedHex}
          onSelect={onSelect}
          onDeselect={onDeselect}
        />
      ))}
    </MapContainer>
  )
}

/** Renders nothing; exists to subscribe to map clicks. react-leaflet hooks only work inside <MapContainer>. */
function ClickToSetCentre({ onCentreChange }: Pick<FlightMapProps, 'onCentreChange'>) {
  useMapEvents({
    click(event) {
      // wrap(): after panning sideways past the antimeridian Leaflet reports longitudes like 190 or -250,
      // which our API (rightly) rejects. wrap() maps them back into -180..180.
      const { lat, lng } = event.latlng.wrap()
      onCentreChange({ lat: round(lat), lon: round(lng) })
    },
  })
  return null
}

/** 4 decimal places ≈ 11 m: plenty for a click, and keeps URLs and cache keys tidy. */
function round(value: number): number {
  return Math.round(value * 10_000) / 10_000
}
