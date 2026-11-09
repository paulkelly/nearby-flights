import type { ReactNode } from 'react'
import type { Flight } from '../api/flights'
import { displayName, formatAltitude, formatSpeed, formatTrack, formatVerticalRate, routeLine } from '../format'
import { predictAltitudeFt } from '../geo/deadReckoning'
import { AirlineLogo } from './AirlineLogo'

// The pieces shared by the nearest card and the map popup, so the two always look and read the same.
// Only <span>s: the card is a <button>, where HTML allows only "phrasing" content (no div, dl, p...). CSS lays them
// out as blocks and grids. Both containers are a two-column grid: these fill a row each.

/**
 * Who it is, as two grid cells: the logo beside the airline (the heading), the flight's name and the route; then
 * the aircraft type on the right. Flights without an airline use their name as the heading instead.
 */
/** Hover text explaining where each kind of route line comes from. */
const ROUTE_LINE_TITLES = {
  route: 'Usual route for this flight number; can occasionally be out of date',
  guess: "Worked out from the aircraft's position, height and climb or descent: no matching route on record",
  unknown: 'No route on record, and nothing to work one out from',
}

export function FlightIdentity({ flight, logoSize }: { flight: Flight; logoSize: number }) {
  const route = routeLine(flight, 'long')
  return (
    <>
      <span className="flight-info__identity">
        <AirlineLogo code={flight.airlineCode} size={logoSize} />
        <span className="flight-info__titles">
          <span className="flight-info__title">{flight.airline ?? displayName(flight)}</span>
          {flight.airline && <span className="flight-info__name">{displayName(flight)}</span>}
          <span className={`flight-info__route flight-info__route--${route.source}`} title={ROUTE_LINE_TITLES[route.source]}>
            {route.text}
          </span>
        </span>
      </span>
      <span className="flight-info__type">{flight.aircraftType ?? ''}</span>
    </>
  )
}

/**
 * What it's doing, each value with its label, across the full width. `ageMs` (how old the report is) dead-reckons
 * the altitude, so it keeps moving between polls when the caller re-renders every second. `children` adds more
 * details at the end.
 */
export function FlightDetails({ flight, ageMs, children }: { flight: Flight; ageMs: number; children?: ReactNode }) {
  return (
    <span className="flight-info__details">
      <Detail label="Altitude" value={formatAltitude({ ...flight, altitudeFt: predictAltitudeFt(flight, ageMs) })} />
      {flight.groundSpeedKt !== null && <Detail label="Speed" value={formatSpeed(flight.groundSpeedKt)} />}
      {flight.trackDeg !== null && <Detail label="Track" value={formatTrack(flight.trackDeg)} />}
      {/* "Vertical speed": the pilot's term (their Vertical Speed Indicator); ADS-B calls it vertical rate. */}
      {!flight.onGround && flight.verticalRateFpm !== null && (
        <Detail label="Vertical speed" value={formatVerticalRate(flight.verticalRateFpm)} />
      )}
      {children}
    </span>
  )
}

export function Detail({ label, value }: { label: string; value: string }) {
  return (
    <span className="flight-info__detail">
      <span className="flight-info__detail-label">{label}</span>
      <span className="flight-info__detail-value">{value}</span>
    </span>
  )
}
