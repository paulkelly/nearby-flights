/**
 * How long a `Retry-After` header asks us to wait, in ms, or `undefined` if it's missing or unreadable.
 *
 * The header has two forms (RFC 9110): a number of seconds ("2"), which is what our API sends, or an HTTP date
 * ("Wed, 21 Oct 2026 07:28:00 GMT"), which proxies and other servers may send. A date in the past means "now" (0).
 */
export function parseRetryAfterMs(header: string | null, nowMs: number): number | undefined {
  if (header === null || header.trim() === '') {
    return undefined
  }

  const value = header.trim()
  if (/^\d+$/.test(value)) {
    return Number(value) * 1000
  }

  // Only the standard HTTP date format: browsers' Date.parse is lenient enough to read "1.5" as 5 January 2001.
  if (!HTTP_DATE.test(value)) {
    return undefined
  }
  const dateMs = Date.parse(value)
  return Number.isNaN(dateMs) ? undefined : Math.max(0, dateMs - nowMs)
}

/** IMF-fixdate, the form HTTP uses: "Fri, 06 Nov 2026 12:00:05 GMT". */
const HTTP_DATE = /^[A-Z][a-z]{2}, \d{2} [A-Z][a-z]{2} \d{4} \d{2}:\d{2}:\d{2} GMT$/
