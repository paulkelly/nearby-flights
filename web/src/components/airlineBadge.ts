/**
 * A stable colour for an airline's fallback badge (shown when it has no logo): the same code always gets the same
 * hue, so a badge is recognisable from one view to the next, and different airlines usually differ.
 *
 * A tiny string hash (like the classic Java hashCode): multiply-and-add over the characters. Not cryptographic,
 * just well-spread enough for 3-letter codes.
 */
export function badgeHue(code: string): number {
  let hash = 0
  for (const char of code) {
    hash = (hash * 31 + char.charCodeAt(0)) % 360
  }
  return hash
}

/** The logo's URL on our API, which fetches and caches it (see LogoEndpoints in the backend). */
export function logoUrl(code: string): string {
  return `/api/airlines/${encodeURIComponent(code)}/logo`
}
