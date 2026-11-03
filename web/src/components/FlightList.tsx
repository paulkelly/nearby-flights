import { useEffect, useRef } from 'react'
import type { Flight } from '../api/flights'
import { displayName, formatAltitude, formatDistance } from '../format'

interface FlightListProps {
  flights: Flight[]
  selectedHex: string | null
  onSelect: (hex: string) => void
}

/** Aircraft nearest first (the API already sorts them). Clicking a row selects that aircraft on the map. */
export function FlightList({ flights, selectedHex, onSelect }: FlightListProps) {
  const listRef = useRef<HTMLUListElement>(null)

  // When the selection changes (e.g. a marker was clicked on the map), scroll its row into view.
  // useRef gives a handle to the real DOM element, for the rare things React doesn't do declaratively.
  useEffect(() => {
    listRef.current
      ?.querySelector('[aria-pressed="true"]')
      ?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
  }, [selectedHex])

  if (flights.length === 0) {
    return <p className="empty">No aircraft in this area right now.</p>
  }

  return (
    <ul className="flight-list" ref={listRef}>
      {flights.map((flight) => (
        <li key={flight.hex}>
          {/* A real <button>: keyboard focus, Enter/Space and screen readers work without extra code. */}
          <button
            type="button"
            className="flight-row"
            aria-pressed={flight.hex === selectedHex}
            onClick={() => onSelect(flight.hex)}
          >
            <span className="flight-row__name">{displayName(flight)}</span>
            <span className="flight-row__type">{flight.aircraftType ?? ''}</span>
            <span className="flight-row__altitude">{formatAltitude(flight)}</span>
            <span className="flight-row__distance">{formatDistance(flight.distanceKm)}</span>
          </button>
        </li>
      ))}
    </ul>
  )
}
