/**
 * The browser's owner token: the key to its saved locations (see the API's /api/owners). Kept in localStorage, so it
 * lasts until the user clears this site's data, and then the saved locations can't be reached any more.
 *
 * Like the rest of localStorage, any script running on this site could read it. That's acceptable for an anonymous
 * token guarding place names, and it's one reason the page loads no third-party scripts.
 */
export const OWNER_TOKEN_KEY = 'flight-radar:owner-token'

/** The parts of the browser's Storage we use, so tests can pass a plain object instead. */
export type TokenStorage = Pick<Storage, 'getItem' | 'setItem' | 'removeItem'>

/** window.localStorage, or undefined where touching it throws (storage blocked for this site). */
export function browserStorage(): TokenStorage | undefined {
  try {
    return window.localStorage
  } catch {
    return undefined
  }
}

/** The stored token, or null if there's none or storage can't be read (every failure means "no token"). */
export function readOwnerToken(storage: TokenStorage | undefined = browserStorage()): string | null {
  try {
    const token = storage?.getItem(OWNER_TOKEN_KEY)
    return token ? token : null
  } catch {
    return null
  }
}

/**
 * Stores the token. Returns false if storage refused: the locations still save, but this browser won't find them
 * again after a reload, which the caller can't fix, only accept.
 */
export function writeOwnerToken(token: string, storage: TokenStorage | undefined = browserStorage()): boolean {
  try {
    storage?.setItem(OWNER_TOKEN_KEY, token)
    return storage !== undefined
  } catch {
    return false
  }
}

export function clearOwnerToken(storage: TokenStorage | undefined = browserStorage()): void {
  try {
    storage?.removeItem(OWNER_TOKEN_KEY)
  } catch {
    // Nothing useful to do.
  }
}
