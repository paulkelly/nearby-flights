import { useEffect, useState } from 'react'

/**
 * The current time (ms), refreshed every `intervalMs`. Components using it re-render on that tick, like a
 * fixed-rate update in a game loop, as opposed to the per-frame requestAnimationFrame loop the markers use.
 */
export function useNow(intervalMs: number): number {
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), intervalMs)
    return () => clearInterval(timer)
  }, [intervalMs])

  return now
}
