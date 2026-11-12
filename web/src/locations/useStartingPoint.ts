import { useEffect, useRef, useState } from 'react'
import type { LatLon } from '../api/flights'
import type { SavedLocation } from '../api/locations'
import { currentPosition } from '../hooks/useGeolocation'

/** Where the first search goes. */
export type StartingPoint =
  | { kind: 'saved'; location: SavedLocation }
  | { kind: 'here'; position: LatLon }
  /** No saved locations and no position (refused, unavailable, or no answer in time): the app's default. */
  | { kind: 'default' }

/**
 * - `saved`: waiting for the saved locations list.
 * - `locating`: none saved, waiting for the device's position.
 * - `done`: started (or the user chose a place themselves first).
 */
export type StartPhase = 'saved' | 'locating' | 'done'

/**
 * How long to wait for a position before giving up on it. The browser's own timeout doesn't cover the time its
 * permission question is on screen, so without this an ignored question would leave the app waiting for ever.
 */
export const LOCATE_ON_START_TIMEOUT_MS = 10_000

/**
 * Picks where the app starts, once: the first saved location if there is one, else the device's position as if
 * "Use my location" had been pressed, else (refused, failed or too slow) the default. Like a game choosing its
 * spawn point: last save, else the player's position, else the level's default spawn.
 *
 * Calls `onStart` exactly once, unless `cancel` comes first: the user picking a place themselves (a map click, a
 * saved location, the locate button) always wins over an answer that arrives later.
 *
 * Failures are silent here: the user didn't press anything, so a "permission denied" message would be noise. The
 * "Use my location" button still explains them if pressed.
 */
export function useStartingPoint(
  saved: { loaded: boolean; locations: SavedLocation[] },
  onStart: (start: StartingPoint) => void,
): { phase: StartPhase; cancel: () => void } {
  const [phase, setPhase] = useState<StartPhase>('saved')

  // The latest callback, for the position step's answer (its effect shouldn't restart when App re-renders).
  const onStartRef = useRef(onStart)
  useEffect(() => {
    onStartRef.current = onStart
  })

  // Step 1: a saved location, as soon as the list is known. Decided during render, not in an effect: nothing
  // outside React is involved, and React allows a component to update its own state while rendering (it renders
  // again straight away, before anything reaches the screen). `onStart` only sets App's state, and App is the
  // component calling this hook. The phase check makes it happen once.
  if (phase === 'saved' && saved.loaded) {
    const first = saved.locations[0]
    if (first) {
      setPhase('done')
      onStart({ kind: 'saved', location: first })
    } else {
      setPhase('locating')
    }
  }

  // Step 2: none saved, so ask where the device is (the browser may ask the user first), with a time limit.
  useEffect(() => {
    if (phase !== 'locating') return

    // "Already answered or cancelled", for whichever of the position and the timer comes second.
    const settled = new AbortController()
    const finish = (start: StartingPoint) => {
      if (settled.signal.aborted) return
      settled.abort()
      setPhase('done')
      onStartRef.current(start)
    }
    const timer = setTimeout(() => finish({ kind: 'default' }), LOCATE_ON_START_TIMEOUT_MS)
    currentPosition().then(
      (position) => finish({ kind: 'here', position }),
      () => finish({ kind: 'default' }),
    )

    return () => {
      settled.abort() // cancelled, or StrictMode's trial unmount: a late answer must not move the map
      clearTimeout(timer)
    }
  }, [phase])

  return { phase, cancel: () => setPhase('done') }
}
