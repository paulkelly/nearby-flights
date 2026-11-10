import { useSyncExternalStore } from 'react'

function subscribe(onChange: () => void): () => void {
  document.addEventListener('visibilitychange', onChange)
  return () => document.removeEventListener('visibilitychange', onChange)
}

/**
 * Whether this tab is showing (not minimised, not a background tab, screen not locked). useSyncExternalStore is
 * React's way to read a value that lives outside React (here, the browser's) and re-render when it changes.
 */
export function useDocumentVisible(): boolean {
  return useSyncExternalStore(subscribe, () => document.visibilityState === 'visible')
}
