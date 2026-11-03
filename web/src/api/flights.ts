// Types come from schema.d.ts, generated from the API's OpenAPI document. Never edit that file by hand:
// start the API, then run `npm run gen:api` to regenerate it after a backend contract change.
// (The script runs openapi-typescript via npx with its own TypeScript 5, because it doesn't support
// this project's TypeScript 7 yet.)
import type { components } from './schema'

export type FlightsResponse = components['schemas']['FlightsResponse']
export type Flight = components['schemas']['FlightDto']

export interface LatLon {
  lat: number
  lon: number
}

/** An error response from our API, carrying the ProblemDetails title when there is one. */
export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

/**
 * Calls GET /api/flights. A relative URL: in development Vite proxies it to the API, in production nginx will.
 * `signal` lets the caller cancel the request (e.g. TanStack Query does when the centre changes mid-flight).
 */
export async function fetchFlights(centre: LatLon, radiusKm: number, signal?: AbortSignal): Promise<FlightsResponse> {
  const query = new URLSearchParams({
    lat: String(centre.lat),
    lon: String(centre.lon),
    radiusKm: String(radiusKm),
  })

  const response = await fetch(`/api/flights?${query}`, { signal })

  if (!response.ok) {
    throw new ApiError(response.status, await problemTitle(response))
  }

  return (await response.json()) as FlightsResponse
}

/** Our API returns errors as ProblemDetails JSON; fall back to the status text if the body isn't that. */
async function problemTitle(response: Response): Promise<string> {
  try {
    const problem = (await response.json()) as { title?: unknown }
    if (typeof problem.title === 'string') {
      return problem.title
    }
  } catch {
    // Not JSON, e.g. the Vite proxy's own error page when the API isn't running.
  }
  return response.statusText || `HTTP ${response.status}`
}
