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

/**
 * Asks the browser for the device's position once. Browsers only allow this in a secure context (HTTPS or
 * localhost) and after the user grants permission, so the failure cases matter as much as the success one.
 */
export function useGeolocation(onLocated: (position: LatLon) => void) {
  const [state, setState] = useState<GeolocationState>({ status: 'idle' })

  const locate = () => {
    // Checked up front: in an insecure context some browsers fail with a vague error, others never call back.
    if (!window.isSecureContext) {
      setState({ status: 'error', message: INSECURE_CONTEXT_MESSAGE })
      return
    }
    if (!('geolocation' in navigator)) {
      setState({ status: 'error', message: 'This browser can’t share your location.' })
      return
    }

    setState({ status: 'locating' })
    navigator.geolocation.getCurrentPosition(
      ({ coords }) => {
        setState({ status: 'idle' })
        onLocated({ lat: coords.latitude, lon: coords.longitude })
      },
      (error) => setState({ status: 'error', message: describeGeolocationError(error.code) }),
      {
        enableHighAccuracy: false, // a network/Wi-Fi fix is plenty for a search centre, and much faster than GPS
        timeout: 15_000,
        maximumAge: 60_000, // a fix from the last minute is fine
      },
    )
  }

  return { state, locate }
}
