import type { VisibleFlight } from '../flights/visibleFlights'
import { formatDistance } from '../format'
import { FlightDetails, FlightIdentity } from './FlightInfo'

interface NearestFlightCardProps {
  nearest: VisibleFlight | null
  /** How old the aircraft's last report is (ms), for dead-reckoning its altitude. */
  ageMs: number
  selectedHex: string | null
  hoveredHex: string | null
  onSelect: (hex: string) => void
  /**
   * Reports whether the card itself is hovered (or focused), not which aircraft: the card's aircraft can change
   * while the pointer rests on it, without any mouseleave. App works out which aircraft that highlights.
   */
  onHoverChange: (hovered: boolean) => void
}

/**
 * "What's that plane?": the one aircraft nearest the centre, chosen by useNearestFlight (prefers aircraft in the
 * air, and doesn't flicker between near-equal ones). Clicking it selects the aircraft on the map, like a list row.
 * On phones it stays visible in the collapsed bottom sheet, so it works without opening the list.
 */
export function NearestFlightCard({
  nearest,
  ageMs,
  selectedHex,
  hoveredHex,
  onSelect,
  onHoverChange,
}: NearestFlightCardProps) {
  return (
    // The hover handlers sit on the section, which is always rendered, rather than the button, which disappears
    // when nothing is in range: an element removed from under the pointer never gets its mouseleave, so the
    // hover state would stay on. React's onFocus/onBlur bubble, so the section also hears the button's focus.
    <section
      className="nearest"
      aria-label="Nearest aircraft"
      onMouseEnter={() => onHoverChange(true)}
      onMouseLeave={() => onHoverChange(false)}
      onFocus={() => onHoverChange(true)}
      onBlur={() => onHoverChange(false)}
    >
      {nearest ? (
        <NearestCardButton
          nearest={nearest}
          ageMs={ageMs}
          selectedHex={selectedHex}
          hoveredHex={hoveredHex}
          onSelect={onSelect}
        />
      ) : (
        <div className="nearest__empty">
          <p className="nearest__label">Nearest aircraft</p>
          <p className="nearest__none">None in range right now.</p>
        </div>
      )}
    </section>
  )
}

function NearestCardButton({
  nearest,
  ageMs,
  selectedHex,
  hoveredHex,
  onSelect,
}: Pick<NearestFlightCardProps, 'ageMs' | 'selectedHex' | 'hoveredHex' | 'onSelect'> & { nearest: VisibleFlight }) {
  const { flight, distanceKm } = nearest
  const hex = flight.hex

  return (
    <button
      type="button"
      className={hex === hoveredHex ? 'nearest__card nearest__card--hovered' : 'nearest__card'}
      aria-pressed={hex === selectedHex}
      onClick={() => onSelect(hex)}
    >
      <span className="nearest__label">Nearest aircraft</span>
      <span className="nearest__distance">{formatDistance(distanceKm)}</span>
      {/* Shared with the map popup. The altitude keeps moving because this re-renders every second. */}
      <FlightIdentity flight={flight} logoSize={44} />
      <FlightDetails flight={flight} ageMs={ageMs} />
    </button>
  )
}
