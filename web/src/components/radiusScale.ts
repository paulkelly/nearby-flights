// The radius slider's scale: logarithmic, so each bit of slider travel multiplies the radius by the same factor.
// Like a camera zoom slider in a game: going 1 → 2 km takes as much travel as 100 → 200 km, which gives fine
// control at small radii ("what's that plane outside my window?") without losing the large ones. The middle of
// the slider is the geometric mean of the ends: √(1 × 250) ≈ 16 km.
//
// The <input type="range"> works in "positions" (0 to SLIDER_POSITIONS); these pure functions convert between
// positions and kilometres. Kilometres are rounded to tidy values, so the label reads 45 km rather than 44.7 km.

/** Smallest radius on the slider: roughly "overhead". */
export const MIN_RADIUS_KM = 1

/**
 * The API's maximum search radius. Must match `FlightsEndpoints.MaxRadiusKm` in the backend: larger values get a
 * 400 response. A product choice, kept below adsb.lol's own 250 nm (≈ 463 km) limit.
 */
export const MAX_RADIUS_KM = 250

/** Slider resolution. Fine enough that every tidy value below has its own position. */
export const SLIDER_POSITIONS = 1000

const RANGE_RATIO = MAX_RADIUS_KM / MIN_RADIUS_KM

/**
 * Gap between neighbouring tidy values at this radius: 1 km below 20 km, 5 km below 100 km, then 10 km.
 */
function stepKmAt(km: number): number {
  if (km < 20) return 1
  if (km < 100) return 5
  return 10
}

/** The nearest tidy value, within the slider's range. */
export function roundRadiusKm(km: number): number {
  const step = stepKmAt(km)
  const rounded = Math.round(km / step) * step
  return Math.min(MAX_RADIUS_KM, Math.max(MIN_RADIUS_KM, rounded))
}

/** Slider position → radius: MIN · ratio^(position/positions), then tidied. */
export function radiusKmAt(position: number): number {
  const fraction = Math.min(1, Math.max(0, position / SLIDER_POSITIONS))
  return roundRadiusKm(MIN_RADIUS_KM * RANGE_RATIO ** fraction)
}

/** Radius → slider position: the inverse of radiusKmAt (a logarithm), rounded to a whole position. */
export function sliderPositionFor(km: number): number {
  const clamped = Math.min(MAX_RADIUS_KM, Math.max(MIN_RADIUS_KM, km))
  return Math.round((SLIDER_POSITIONS * Math.log(clamped / MIN_RADIUS_KM)) / Math.log(RANGE_RATIO))
}

/**
 * The next tidy value up or down, for arrow keys. Needed because one arrow press moves the native slider by a
 * single position (0.1% of the travel), which never reaches the next tidy value, so the key would do nothing.
 */
export function stepRadiusKm(km: number, direction: 1 | -1): number {
  // Going down, use the step size just below this value: from 20 km the next value down is 19, not 15.
  const step = direction === 1 ? stepKmAt(km) : stepKmAt(km - 1)
  return roundRadiusKm(km + direction * step)
}
