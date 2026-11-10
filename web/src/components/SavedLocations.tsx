import { useState, type FormEvent } from 'react'
import { ApiError, type LatLon } from '../api/flights'
import type { SavedLocation } from '../api/locations'
import { isCurrentView, MAX_SAVED_LOCATIONS } from '../locations/savedLocations'
import { useSavedLocations } from '../locations/useSavedLocations'

interface SavedLocationsProps {
  /** The current search, which "Save this location" saves. */
  centre: LatLon
  radiusKm: number
  /** Show a saved location: move the search (and the map) there. */
  onGo: (location: SavedLocation) => void
}

/** Network failures surface as a TypeError ("Failed to fetch"); say something a person can act on instead. */
function describe(error: Error): string {
  return error instanceof ApiError ? error.message : 'Couldn’t reach the server. Check your connection.'
}

export function SavedLocations({ centre, radiusKm, onGo }: SavedLocationsProps) {
  const { locations, loadError, save, remove } = useSavedLocations()
  const [naming, setNaming] = useState(false)
  const [name, setName] = useState('')

  const full = locations.length >= MAX_SAVED_LOCATIONS
  const error = save.error ?? remove.error ?? loadError

  const startNaming = () => {
    save.reset() // clear an earlier attempt's error
    setNaming(true)
  }

  const stopNaming = () => {
    save.reset()
    setNaming(false)
    setName('')
  }

  const submit = (event: FormEvent) => {
    event.preventDefault() // a form submit would otherwise reload the page
    save.mutate(
      { name: name.trim(), lat: centre.lat, lon: centre.lon, radiusKm },
      { onSuccess: stopNaming }, // only this call's success: on failure the form stays, with the API's reason
    )
  }

  const confirmDelete = (location: SavedLocation) => {
    if (window.confirm(`Delete “${location.name}”?`)) {
      remove.mutate(location.id)
    }
  }

  return (
    <section className="saved" aria-labelledby="saved-heading">
      <h2 id="saved-heading" className="saved__heading">
        Saved locations
      </h2>

      {locations.length > 0 && (
        <ul className="saved__list">
          {locations.map((location) => {
            const current = isCurrentView(location, centre, radiusKm)
            return (
              <li key={location.id} className={`saved__chip${current ? ' saved__chip--current' : ''}`}>
                <button
                  type="button"
                  className="saved__go"
                  aria-current={current ? 'location' : undefined}
                  title={`Show ${location.radiusKm} km around ${location.name}`}
                  onClick={() => onGo(location)}
                >
                  {location.name}
                </button>
                <button
                  type="button"
                  className="saved__delete"
                  aria-label={`Delete ${location.name}`}
                  disabled={remove.isPending}
                  onClick={() => confirmDelete(location)}
                >
                  ×
                </button>
              </li>
            )
          })}
        </ul>
      )}

      {naming ? (
        <form className="saved__form" onSubmit={submit}>
          <input
            className="saved__name"
            aria-label="Name for this location"
            placeholder="Name, e.g. Home"
            maxLength={50}
            autoFocus
            value={name}
            onChange={(event) => setName(event.target.value)}
            onKeyDown={(event) => event.key === 'Escape' && stopNaming()}
          />
          <button type="submit" className="saved__button" disabled={save.isPending || name.trim() === ''}>
            {save.isPending ? 'Saving…' : 'Save'}
          </button>
          <button type="button" className="saved__button" onClick={stopNaming}>
            Cancel
          </button>
        </form>
      ) : (
        <button type="button" className="saved__button saved__add" disabled={full} onClick={startNaming}>
          <span aria-hidden="true">☆</span> Save this location
        </button>
      )}

      {full && !naming && <p className="saved__note">{MAX_SAVED_LOCATIONS} saved: delete one to save another.</p>}
      {error && (
        <p role="alert" className="saved__error">
          {describe(error)}
        </p>
      )}
    </section>
  )
}
