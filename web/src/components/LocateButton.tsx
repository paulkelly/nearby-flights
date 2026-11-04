import type { GeolocationState } from '../hooks/useGeolocation'

interface LocateButtonProps {
  state: GeolocationState
  onLocate: () => void
}

export function LocateButton({ state, onLocate }: LocateButtonProps) {
  const locating = state.status === 'locating'

  return (
    <div className="locate">
      <button type="button" className="locate__button" onClick={onLocate} disabled={locating}>
        <span aria-hidden="true">📍</span> {locating ? 'Finding you…' : 'Use my location'}
      </button>
      {/* role="status": screen readers announce the message when it appears, without stealing focus. */}
      {state.status === 'error' && (
        <p role="status" className="locate__error">
          {state.message}
        </p>
      )}
    </div>
  )
}
