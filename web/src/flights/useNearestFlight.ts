import { useState } from 'react'
import { chooseNearest, type NearestChoice } from './nearestFlight'
import type { VisibleFlight } from './visibleFlights'

/**
 * The aircraft for the "nearest" card, re-chosen whenever the visible set changes (once a second). Unlike the
 * list, the choice depends on the previous one (hysteresis, the landing grace period), so it needs memory.
 *
 * The memory is state, updated *during render* when `visible` changes: React's documented pattern for
 * "storing information from previous renders". It's like reading last frame's value in a game update loop.
 * - Not useEffect: an effect runs after the render is on screen, so the card would show last second's choice
 *   for one frame and then re-render.
 * - Not useRef: a ref written during render can go wrong when React renders twice (Strict Mode, or a render
 *   it throws away), and changing it doesn't re-render anything.
 * Calling setState here is fine because it's guarded by `visible !== state.visible`: React re-runs this
 * component straight away with the new state, before showing anything, and the guard is then false.
 */
export function useNearestFlight(visible: VisibleFlight[], now: number): VisibleFlight | null {
  const [state, setState] = useState<{ visible: VisibleFlight[]; choice: NearestChoice }>(() => ({
    visible,
    choice: chooseNearest(visible, null, now),
  }))

  if (visible !== state.visible) {
    const choice = chooseNearest(visible, state.choice.tracking, now)
    setState({ visible, choice })
    return choice.nearest
  }

  return state.choice.nearest
}
