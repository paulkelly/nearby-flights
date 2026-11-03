import { useEffect, useRef } from 'react'
import type { Flight } from '../api/flights'
import { displayName, formatAltitude, formatDistance } from '../format'

interface FlightListProps {
  flights: Flight[]
  selectedHex: string | null
  hoveredHex: string | null
  onSelect: (hex: string) => void
  onHover: (hex: string) => void
  onUnhover: (hex: string) => void
}

/** Aircraft nearest first (the API already sorts them). Clicking a row selects that aircraft on the map. */
export function FlightList({ flights, selectedHex, hoveredHex, onSelect, onHover, onUnhover }: FlightListProps) {
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
            // Not CSS :hover alone: the row must also light up when its plane is hovered on the map.
            className={flight.hex === hoveredHex ? 'flight-row flight-row--hovered' : 'flight-row'}
            aria-pressed={flight.hex === selectedHex}
            onClick={() => onSelect(flight.hex)}
            onMouseEnter={() => onHover(flight.hex)}
            onMouseLeave={() => onUnhover(flight.hex)}
            // Keyboard users get the same map highlight when tabbing through the rows.
            onFocus={() => onHover(flight.hex)}
            onBlur={() => onUnhover(flight.hex)}
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
