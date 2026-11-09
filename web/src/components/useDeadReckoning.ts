import { useLayoutEffect } from 'react'
import type { Flight, LatLon } from '../api/flights'
import { canExtrapolate, positionAgeMs, predictPosition } from '../geo/deadReckoning'

/**
 * Dead reckoning, game-loop style: every animation frame, call `apply` with the aircraft's predicted position, so
 * it can move a Leaflet layer there directly. This deliberately bypasses React: re-rendering ~100 components 60
 * times a second would be wasteful, while moving a Leaflet layer is cheap. React decides *what* exists; this loop
 * decides where it's drawn. Used by the aircraft markers and by the line to the tracked aircraft, so both move in
 * step.
 *
 * Does nothing for aircraft we don't extrapolate (see canExtrapolate) or for `flight` null: the layer then stays
 * at the position React gave it.
 *
 * `apply` must be stable (useCallback), or the loop restarts on every render.
 *
 * useLayoutEffect (not useEffect) so the first predicted position is applied before the browser paints.
 * Otherwise each poll would show one frame at the older reported position, a visible backwards twitch.
 */
export function useDeadReckoning(
  flight: Flight | null,
  fetchedAt: string,
  receivedAtMs: number,
  apply: (position: LatLon) => void,
): void {
  useLayoutEffect(() => {
    if (!flight || !canExtrapolate(flight)) return

    let frameId = 0
    const tick = () => {
      const ageMs = positionAgeMs(flight.positionTimestamp, fetchedAt, receivedAtMs, Date.now())
      apply(predictPosition(flight, ageMs))
      frameId = requestAnimationFrame(tick) // the browser calls tick once per frame; paused in background tabs
    }
    tick()

    // A new poll (new `flight` object) or unmount stops this loop; the next run starts from the fresh report.
    return () => cancelAnimationFrame(frameId)
  }, [flight, fetchedAt, receivedAtMs, apply])
}
