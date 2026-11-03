import { Marker, Popup } from 'react-leaflet'
import type { Flight } from '../api/flights'
import { displayName, formatAltitude, formatSpeed, formatTrack, formatVerticalRate } from '../format'
import { aircraftIcon } from './aircraftIcon'

interface AircraftMarkerProps {
  flight: Flight
}

export function AircraftMarker({ flight }: AircraftMarkerProps) {
  return (
    <Marker
      position={[flight.lat, flight.lon]}
      icon={aircraftIcon(flight.trackDeg, flight.onGround)}
      title={displayName(flight)} // native hover tooltip
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
