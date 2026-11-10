import { describe, expect, it } from 'vitest'
import { clearOwnerToken, OWNER_TOKEN_KEY, readOwnerToken, writeOwnerToken, type TokenStorage } from './ownerToken'

/** An in-memory stand-in for localStorage. */
function memoryStorage(initial: Record<string, string> = {}): TokenStorage & { data: Record<string, string> } {
  const data = { ...initial }
  return {
    data,
    getItem: (key) => data[key] ?? null,
    setItem: (key, value) => {
      data[key] = value
    },
    removeItem: (key) => {
      delete data[key]
    },
  }
}

/** Storage that throws on every call, as browsers do when storage is blocked or full. */
const brokenStorage: TokenStorage = {
  getItem: () => {
    throw new Error('SecurityError')
  },
  setItem: () => {
    throw new Error('QuotaExceededError')
  },
  removeItem: () => {
    throw new Error('SecurityError')
  },
}

describe('owner token storage', () => {
  it('reads back what it wrote', () => {
    const storage = memoryStorage()

    expect(writeOwnerToken('abc', storage)).toBe(true)
    expect(readOwnerToken(storage)).toBe('abc')
    expect(storage.data[OWNER_TOKEN_KEY]).toBe('abc')
  })

  it('has no token until one is written, and none after clearing', () => {
    const storage = memoryStorage()
    expect(readOwnerToken(storage)).toBeNull()

    writeOwnerToken('abc', storage)
    clearOwnerToken(storage)

    expect(readOwnerToken(storage)).toBeNull()
  })

  it('treats an empty value as no token', () => {
    expect(readOwnerToken(memoryStorage({ [OWNER_TOKEN_KEY]: '' }))).toBeNull()
  })

  it('never throws when storage is blocked or missing', () => {
    expect(readOwnerToken(brokenStorage)).toBeNull()
    expect(writeOwnerToken('abc', brokenStorage)).toBe(false)
    expect(() => clearOwnerToken(brokenStorage)).not.toThrow()
    expect(writeOwnerToken('abc', undefined)).toBe(false)
  })
})
