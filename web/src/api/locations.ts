import { ApiError } from './flights'
import type { components } from './schema'

export type SavedLocation = components['schemas']['SavedLocationDto']
/** What we send. The schema allows nulls (so the API can name missing fields); we always send every field. */
export type SavedLocationInput = { [K in keyof components['schemas']['SavedLocationRequest']]: NonNullable<
  components['schemas']['SavedLocationRequest'][K]
> }

/** POST /api/owners: a new anonymous owner. The token is shown only this once, so the caller must store it. */
export async function createOwner(): Promise<string> {
  const response = await fetch('/api/owners', { method: 'POST' })
  await throwIfFailed(response)
  const body = (await response.json()) as components['schemas']['NewOwnerResponse']
  return body.token
}

export async function fetchSavedLocations(token: string, signal?: AbortSignal): Promise<SavedLocation[]> {
  const response = await fetch('/api/locations', { headers: authorization(token), signal })
  await throwIfFailed(response)
  return (await response.json()) as SavedLocation[]
}

export async function createSavedLocation(token: string, input: SavedLocationInput): Promise<SavedLocation> {
  const response = await fetch('/api/locations', {
    method: 'POST',
    headers: { ...authorization(token), 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  await throwIfFailed(response)
  return (await response.json()) as SavedLocation
}

export async function deleteSavedLocation(token: string, id: string): Promise<void> {
  const response = await fetch(`/api/locations/${encodeURIComponent(id)}`, {
    method: 'DELETE',
    headers: authorization(token),
  })
  // 404: already gone (deleted in another tab, say). That's what the user wanted, so not an error.
  if (response.status !== 404) {
    await throwIfFailed(response)
  }
}

function authorization(token: string): HeadersInit {
  return { Authorization: `Bearer ${token}` }
}

async function throwIfFailed(response: Response): Promise<void> {
  if (!response.ok) {
    throw new ApiError(response.status, await problemMessage(response))
  }
}

/**
 * The most useful sentence in a ProblemDetails body. For these endpoints that's usually `detail` ("You already have a
 * location called Home."), or the first validation error, rather than the generic `title`.
 */
export async function problemMessage(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { title?: unknown; detail?: unknown; errors?: unknown }
    if (problem.errors && typeof problem.errors === 'object') {
      const first = Object.values(problem.errors as Record<string, unknown>).flat()[0]
      if (typeof first === 'string') {
        return first
      }
    }
    if (typeof problem.detail === 'string') {
      return problem.detail
    }
    if (typeof problem.title === 'string') {
      return problem.title
    }
  } catch {
    // Not JSON, e.g. the Vite proxy's own error page when the API isn't running.
  }
  return response.statusText || `HTTP ${response.status}`
}
