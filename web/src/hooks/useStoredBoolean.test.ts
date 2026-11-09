import { describe, expect, it } from 'vitest'
import { readStoredBoolean, writeStoredBoolean, type BooleanStorage } from './useStoredBoolean'

/** An in-memory stand-in for localStorage. */
function memoryStorage(initial: Record<string, string> = {}): BooleanStorage & { data: Record<string, string> } {
  const data = { ...initial }
  return {
    data,
    getItem: (key) => data[key] ?? null,
    setItem: (key, value) => {
      data[key] = value
    },
  }
}

/** Storage that throws on every call, as browsers do when storage is blocked or full. */
const brokenStorage: BooleanStorage = {
  getItem: () => {
    throw new Error('SecurityError')
  },
  setItem: () => {
    throw new Error('QuotaExceededError')
  },
}

describe('readStoredBoolean', () => {
  it.each([
    ['true', false, true],
    ['false', true, false],
  ])('reads a stored %s', (stored, fallback, expected) => {
    expect(readStoredBoolean(memoryStorage({ key: stored }), 'key', fallback)).toBe(expected)
  })

  it('uses the fallback when nothing is stored', () => {
    expect(readStoredBoolean(memoryStorage(), 'key', true)).toBe(true)
    expect(readStoredBoolean(memoryStorage(), 'key', false)).toBe(false)
  })

  it('uses the fallback for anything other than "true" or "false"', () => {
    expect(readStoredBoolean(memoryStorage({ key: 'yes' }), 'key', false)).toBe(false)
  })

  it('uses the fallback when storage throws or is missing', () => {
    expect(readStoredBoolean(brokenStorage, 'key', true)).toBe(true)
    expect(readStoredBoolean(undefined, 'key', true)).toBe(true)
  })
})

describe('writeStoredBoolean', () => {
  it('stores the value so it reads back', () => {
    const storage = memoryStorage()

    writeStoredBoolean(storage, 'key', true)

    expect(storage.data).toEqual({ key: 'true' })
    expect(readStoredBoolean(storage, 'key', false)).toBe(true)
  })

  it('does nothing (rather than throw) when storage refuses', () => {
    expect(() => writeStoredBoolean(brokenStorage, 'key', true)).not.toThrow()
    expect(() => writeStoredBoolean(undefined, 'key', true)).not.toThrow()
  })
})
