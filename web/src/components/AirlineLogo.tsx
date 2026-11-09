import { useState } from 'react'
import { badgeHue, logoUrl } from './airlineBadge'

interface AirlineLogoProps {
  /** ICAO airline code ("EIN"), or null for flights that aren't airline flights. */
  code: string | null
  /** Displayed size in CSS pixels (the logos are square). */
  size: number
}

/**
 * An airline's logo, or a coloured badge with its code when there's no logo (or it fails to load). For non-airline
 * flights it's an empty space of the same size, so rows stay aligned.
 *
 * Decorative (alt=""): the airline's name is always shown as text next to it, so a screen reader would only hear
 * it twice.
 */
export function AirlineLogo({ code, size }: AirlineLogoProps) {
  if (!code) {
    return <span className="airline-logo" style={{ width: size, height: size }} aria-hidden="true" />
  }

  // key = code: a different airline gets a fresh component, so a previous airline's "failed to load" doesn't stick.
  // (Same idea as the list's key={hex}: React keeps state per key.)
  return <LogoOrBadge key={code} code={code} size={size} />
}

function LogoOrBadge({ code, size }: { code: string; size: number }) {
  const [failed, setFailed] = useState(false)

  if (failed) {
    return (
      <span
        className="airline-logo airline-logo--badge"
        style={{ width: size, height: size, backgroundColor: `hsl(${badgeHue(code)} 45% 40%)`, fontSize: size * 0.32 }}
        aria-hidden="true"
      >
        {code}
      </span>
    )
  }

  return (
    <img
      className="airline-logo"
      src={logoUrl(code)}
      alt=""
      // width/height reserve the space before the image arrives, so nothing jumps when it does.
      width={size}
      height={size}
      // Rows far down the list only load their logo when scrolled near.
      loading="lazy"
      // 404 (no logo) or 503 (source down): show the badge instead of a broken-image icon.
      onError={() => setFailed(true)}
    />
  )
}
