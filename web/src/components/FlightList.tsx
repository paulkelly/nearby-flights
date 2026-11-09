import { useEffect, useRef } from 'react'
import type { VisibleFlight } from '../flights/visibleFlights'
import { displayName, flightSummary, formatAltitude, formatDistance } from '../format'
import { AirlineLogo } from './AirlineLogo'

interface FlightListProps {
  flights: VisibleFlight[]
  /** Shown instead of the list when it's empty. */
  emptyMessage: string
  selectedHex: string | null
  hoveredHex: string | null
  onSelect: (hex: string) => void
  onHover: (hex: string) => void
  onUnhover: (hex: string) => void
}

/**
 * Aircraft inside the circle, nearest first, with distances measured from the current centre (updated each
 * second as the aircraft move). Clicking a row selects that aircraft on the map.
 */
export function FlightList({
  flights,
  emptyMessage,
  selectedHex,
  hoveredHex,
  onSelect,
  onHover,
  onUnhover,
}: FlightListProps) {
  const listRef = useRef<HTMLUListElement>(null)

  // When the selection changes (e.g. a marker was clicked on the map), scroll its row into view.
  // useRef gives a handle to the real DOM element, for the rare things React doesn't do declaratively.
  useEffect(() => {
    listRef.current
      ?.querySelector('[aria-pressed="true"]')
      ?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
  }, [selectedHex])

  if (flights.length === 0) {
    return <p className="empty">{emptyMessage}</p>
  }

  return (
    <ul className="flight-list" ref={listRef}>
      {flights.map(({ flight, distanceKm }) => (
        <li key={flight.hex}>
          {/* A real <button>: keyboard focus, Enter/Space and screen readers work without extra code. */}
          <button
            type="button"
            // Not CSS :hover alone: the row must also light up when its plane is hovered on the map.
            // Airborne rows are tinted so they stand out; grounded ones are faded (like their grey map markers).
            className={[
              'flight-row',
              flight.onGround ? 'flight-row--ground' : 'flight-row--airborne',
              flight.hex === hoveredHex && 'flight-row--hovered',
            ]
              .filter(Boolean)
              .join(' ')}
            aria-pressed={flight.hex === selectedHex}
            onClick={() => onSelect(flight.hex)}
            onMouseEnter={() => onHover(flight.hex)}
            onMouseLeave={() => onUnhover(flight.hex)}
            // Keyboard users get the same map highlight when tabbing through the rows.
            onFocus={() => onHover(flight.hex)}
            onBlur={() => onUnhover(flight.hex)}
          >
            <AirlineLogo code={flight.airlineCode} size={28} />
            {/* Airline first, then the callsign; flights without an airline show just their name. */}
            <span className="flight-row__name">
              {flight.airline && <span className="flight-row__airline">{flight.airline} </span>}
              <span className={flight.airline ? 'flight-row__callsign' : undefined}>{displayName(flight)}</span>
            </span>
            <span className="flight-row__summary">{flightSummary(flight)}</span>
            <span className="flight-row__altitude">{formatAltitude(flight)}</span>
            <span className="flight-row__distance">{formatDistance(distanceKm)}</span>
          </button>
        </li>
      ))}
    </ul>
  )
}
