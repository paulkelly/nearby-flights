import { useState } from 'react'

/** The part of the browser's Storage (localStorage) we use, so tests can pass a plain object instead. */
export type BooleanStorage = Pick<Storage, 'getItem' | 'setItem'>

/**
 * The stored value, or `fallback` if there's none, it's unreadable, or storage isn't available at all. Browsers
 * can refuse storage (some private modes, blocked cookies, a full quota) and even *reading* it can throw, so
 * every failure means "use the default", never a crash.
 */
export function readStoredBoolean(storage: BooleanStorage | undefined, key: string, fallback: boolean): boolean {
  try {
    const value = storage?.getItem(key)
    return value === 'true' ? true : value === 'false' ? false : fallback
  } catch {
    return fallback
  }
}

/** Saves the value if storage allows; if not, it's simply not remembered (the app works the same). */
export function writeStoredBoolean(storage: BooleanStorage | undefined, key: string, value: boolean): void {
  try {
    storage?.setItem(key, String(value))
  } catch {
    // Storage full or blocked: nothing useful to do.
  }
}

/** window.localStorage, or undefined where touching it throws (e.g. storage blocked for this site). */
function browserStorage(): BooleanStorage | undefined {
  try {
    return window.localStorage
  } catch {
    return undefined
  }
}

/**
 * Like useState for a boolean, but remembered in localStorage across reloads (per browser, per site).
 *
 * Read once, when the component first renders (useState's initialiser function runs only then). Written in the
 * setter, i.e. when the user changes it, rather than in an effect: an effect would also write the default on
 * first load, and run for reasons other than the user's choice.
 */
export function useStoredBoolean(key: string, fallback: boolean): [boolean, (value: boolean) => void] {
  const [value, setValue] = useState(() => readStoredBoolean(browserStorage(), key, fallback))

  const update = (next: boolean) => {
    writeStoredBoolean(browserStorage(), key, next)
    setValue(next)
  }

  return [value, update]
}
