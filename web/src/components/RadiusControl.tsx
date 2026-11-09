import type { KeyboardEvent } from 'react'
import { SLIDER_POSITIONS, radiusKmAt, sliderPositionFor, stepRadiusKm } from './radiusScale'

interface RadiusControlProps {
  radiusKm: number
  onChange: (radiusKm: number) => void
}

export function RadiusControl({ radiusKm, onChange }: RadiusControlProps) {
  // Arrow keys step to the next tidy value. Left alone, one press moves the native slider by a single position
  // (0.1% of its travel), which rounds back to the same radius, so the key would appear to do nothing.
  // Home/End and PageUp/PageDown move far enough natively, so they're left to the browser.
  const onKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    const direction =
      event.key === 'ArrowRight' || event.key === 'ArrowUp' ? 1 : event.key === 'ArrowLeft' || event.key === 'ArrowDown' ? -1 : 0
    if (direction === 0) return
    event.preventDefault()
    onChange(stepRadiusKm(radiusKm, direction))
  }

  return (
    <label className="radius-control">
      <span>
        Radius: <strong>{radiusKm} km</strong>
      </span>
      {/*
        A "controlled" input: React state holds the radius, and the input displays the matching position and
        reports changes. The input's value is a position on a logarithmic scale (see radiusScale.ts), not km,
        so aria-valuetext tells screen readers the radius instead of "523".
      */}
      <input
        type="range"
        min={0}
        max={SLIDER_POSITIONS}
        value={sliderPositionFor(radiusKm)}
        aria-valuetext={`${radiusKm} km`}
        onChange={(event) => onChange(radiusKmAt(Number(event.target.value)))}
        onKeyDown={onKeyDown}
      />
    </label>
  )
}
