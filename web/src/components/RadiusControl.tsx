interface RadiusControlProps {
  radiusKm: number
  onChange: (radiusKm: number) => void
}

/** The API's limit: 250 nautical miles, adsb.lol's maximum. */
export const MAX_RADIUS_KM = 463

export function RadiusControl({ radiusKm, onChange }: RadiusControlProps) {
  return (
    <label className="radius-control">
      <span>
        Radius: <strong>{radiusKm} km</strong>
      </span>
      {/*
        A "controlled" input: React state holds the value, and the input just displays it and reports changes.
        The single source of truth is App's state, not the DOM element.
      */}
      <input
        type="range"
        min={1}
        max={MAX_RADIUS_KM}
        value={radiusKm}
        onChange={(event) => onChange(Number(event.target.value))}
      />
    </label>
  )
}
