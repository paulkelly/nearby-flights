import { useEffect, useState } from 'react'

/**
 * Returns `value`, but only after it has stopped changing for `delayMs`.
 * Used so dragging the radius slider triggers one request when you let go, not one per pixel.
 */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    // Cleanup runs before the effect re-runs (value changed again) or on unmount, cancelling the pending update.
    // That cancel-and-restart is the whole debounce.
    return () => clearTimeout(timer)
  }, [value, delayMs])

  return debounced
}
