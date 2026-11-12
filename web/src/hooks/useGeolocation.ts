import { useState } from 'react'
import type { LatLon } from '../api/flights'

export type GeolocationState =
  | { status: 'idle' }
  | { status: 'locating' }
  | { status: 'error'; message: string }

// Codes from the browser's GeolocationPositionError.
const PERMISSION_DENIED = 1
const POSITION_UNAVAILABLE = 2
const TIMEOUT = 3

/** Turns a geolocation failure into something a user can act on. Exported for tests. */
export function describeGeolocationError(code: number): string {
  switch (code) {
    case PERMISSION_DENIED:
      return 'Location permission was denied. Allow it in your browser’s site settings to use this.'
    case POSITION_UNAVAILABLE:
      return 'Your location isn’t available right now. Check that location services are on.'
    case TIMEOUT:
      return 'Finding your location took too long. Try again.'
    default:
      return 'Couldn’t get your location.'
  }
}

export const INSECURE_CONTEXT_MESSAGE =
  'Your location is only available over a secure (HTTPS) connection, which this page isn’t using.'

/** Thrown by currentPosition with text a user can act on. */
export class GeolocationError extends Error {}

/**
 * The device's position, once. Browsers only allow this in a secure context (HTTPS or localhost) and after the user
 * grants permission, so the failure cases matter as much as the success one. Rejects with a GeolocationError.
 *
 * Note the browser's own `timeout` only starts once permission is granted: while its "Allow location?" question is
 * on screen this can wait for as long as the user does. Callers that can't wait set their own limit.
 */
export function currentPosition(): Promise<LatLon> {
  // Checked up front: in an insecure context some browsers fail with a vague error, others never call back.
  if (!window.isSecureContext) {
    return Promise.reject(new GeolocationError(INSECURE_CONTEXT_MESSAGE))
  }
  if (!('geolocation' in navigator)) {
    return Promise.reject(new GeolocationError('This browser can’t share your location.'))
  }

  return new Promise((resolve, reject) =>
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => resolve({ lat: coords.latitude, lon: coords.longitude }),
      (error) => reject(new GeolocationError(describeGeolocationError(error.code))),
      {
        enableHighAccuracy: false, // a network/Wi-Fi fix is plenty for a search centre, and much faster than GPS
        timeout: 15_000,
        maximumAge: 60_000, // a fix from the last minute is fine
      },
    ),
  )
}

/** "Use my location": asks for the device's position and reports progress and failures for the button to show. */
export function useGeolocation(onLocated: (position: LatLon) => void) {
  const [state, setState] = useState<GeolocationState>({ status: 'idle' })

  const locate = () => {
    setState({ status: 'locating' })
    currentPosition().then(
      (position) => {
        setState({ status: 'idle' })
        onLocated(position)
      },
      (error: GeolocationError) => setState({ status: 'error', message: error.message }),
    )
  }

  return { state, locate }
}
