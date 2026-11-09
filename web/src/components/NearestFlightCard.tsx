import type { VisibleFlight } from '../flights/visibleFlights'
import { displayName, formatAltitude, formatDistance, formatSpeed } from '../format'

interface NearestFlightCardProps {
  nearest: VisibleFlight | null
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
export function NearestFlightCard({ nearest, selectedHex, hoveredHex, onSelect, onHoverChange }: NearestFlightCardProps) {
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
        <NearestCardButton nearest={nearest} selectedHex={selectedHex} hoveredHex={hoveredHex} onSelect={onSelect} />
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
  selectedHex,
  hoveredHex,
  onSelect,
}: Pick<NearestFlightCardProps, 'selectedHex' | 'hoveredHex' | 'onSelect'> & { nearest: VisibleFlight }) {
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
      <span className="nearest__name">{displayName(flight)}</span>
      <span className="nearest__type">{flight.aircraftType ?? ''}</span>
      <span className="nearest__detail">
        {formatAltitude(flight)}
        {!flight.onGround && flight.groundSpeedKt !== null && ` · ${formatSpeed(flight.groundSpeedKt)}`}
      </span>
    </button>
  )
}
