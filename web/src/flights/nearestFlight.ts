import type { VisibleFlight } from './visibleFlights'

/**
 * How long a tracked aircraft stays the "nearest" after it lands. Without this, the card would jump to another
 * plane the moment its plane touched down, which feels wrong while you're still watching it roll out. The data
 * updates every ~10 s, so this outlasts at least one poll after the landing.
 */
export const LANDED_GRACE_MS = 15_000

/**
 * Another aircraft must be this much closer (as a fraction of the tracked one's distance) before the card
 * switches to it. Hysteresis, like a target lock in a game: without it, two planes at nearly the same distance
 * would make the card flicker between them every second.
 */
export const SWITCH_MARGIN = 0.1

/** What we remember between updates about the aircraft the card shows. */
export interface NearestTracking {
  hex: string
  /** Whether it was on the ground at the last update, so the next one can tell that it has just landed. */
  onGround: boolean
  /**
   * When it landed while we were tracking it (ms), or null. Stays null for an aircraft that was already on the
   * ground when we picked it (only shown because nothing was flying): that one gets no grace period.
   */
  landedAtMs: number | null
}

export interface NearestChoice {
  nearest: VisibleFlight | null
  tracking: NearestTracking | null
}

/**
 * Picks the aircraft for the "nearest" card. A pure function of the visible aircraft, what was tracked last time
 * and the time, called again on every update (once a second).
 *
 * Rules:
 * - Prefer aircraft in the air: at an airport the nearest aircraft is usually parked at a gate.
 * - If nothing is in the air, show the nearest aircraft on the ground rather than nothing.
 * - Keep the tracked aircraft for LANDED_GRACE_MS after it lands, so it doesn't vanish on touchdown.
 * - Keep the tracked aircraft until another is clearly closer (SWITCH_MARGIN).
 *
 * @param visible the aircraft in the circle, nearest first (as from visibleFlights).
 */
export function chooseNearest(
  visible: readonly VisibleFlight[],
  previous: NearestTracking | null,
  nowMs: number,
): NearestChoice {
  const tracked = previous ? visible.find((v) => v.flight.hex === previous.hex) : undefined

  // Did the tracked aircraft land while we were watching it? Starts now if it was airborne last time.
  const landedAtMs = !tracked?.flight.onGround ? null : previous?.onGround ? previous.landedAtMs : nowMs
  const trackedInGrace = landedAtMs !== null && nowMs - landedAtMs < LANDED_GRACE_MS

  // Who can be shown. Filtering `visible` keeps it nearest first, so the best candidate is simply the first.
  const anyAirborne = visible.some((v) => !v.flight.onGround)
  const eligible = anyAirborne
    ? visible.filter((v) => !v.flight.onGround || (v === tracked && trackedInGrace))
    : visible

  const best = eligible[0]
  if (!best) return { nearest: null, tracking: null }

  // Stick with the tracked aircraft unless the best one is clearly closer.
  const keepTracked =
    tracked !== undefined && eligible.includes(tracked) && best.distanceKm >= tracked.distanceKm * (1 - SWITCH_MARGIN)
  const nearest = keepTracked ? tracked : best

  return {
    nearest,
    tracking: {
      hex: nearest.flight.hex,
      onGround: nearest.flight.onGround,
      landedAtMs: nearest === tracked ? landedAtMs : null,
    },
  }
}
