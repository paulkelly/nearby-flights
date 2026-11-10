import { useState, type FormEvent } from 'react'
import { ApiError, type LatLon } from '../api/flights'
import type { SavedLocation } from '../api/locations'
import { findByName, isCurrentView, MAX_SAVED_LOCATIONS } from '../locations/savedLocations'
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
  const { locations, loadError, save, replace, remove } = useSavedLocations()
  const [naming, setNaming] = useState(false)
  const [name, setName] = useState('')

  const full = locations.length >= MAX_SAVED_LOCATIONS
  const saving = save.isPending || replace.isPending
  const error = save.error ?? replace.error ?? remove.error ?? loadError

  const resetMutations = () => {
    save.reset() // clear an earlier attempt's error
    replace.reset()
  }

  const startNaming = () => {
    resetMutations()
    setNaming(true)
  }

  const stopNaming = () => {
    resetMutations()
    setNaming(false)
    setName('')
  }

  const submit = (event: FormEvent) => {
    event.preventDefault() // a form submit would otherwise reload the page
    const input = { name: name.trim(), lat: centre.lat, lon: centre.lon, radiusKm }

    // A name that's already saved: offer to move that location here instead of failing with "already exists".
    // Checked here, against the list we already have, so the question comes before any request. (Another tab
    // could still take the name meanwhile; then the API's 409 explains.)
    const existing = findByName(locations, input.name)
    if (existing) {
      if (isCurrentView(existing, centre, radiusKm)) {
        stopNaming() // already saved exactly here: nothing to change
        return
      }
      if (window.confirm(`“${existing.name}” is already saved. Replace it with this location?`)) {
        // Only this call's success closes the form: on failure it stays, with the API's reason.
        replace.mutate({ id: existing.id, input }, { onSuccess: stopNaming })
      }
      return
    }

    save.mutate(input, { onSuccess: stopNaming })
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
          <button type="submit" className="saved__button" disabled={saving || name.trim() === ''}>
            {saving ? 'Saving…' : 'Save'}
          </button>
          <button type="button" className="saved__button" onClick={stopNaming}>
            Cancel
          </button>
        </form>
      ) : (
        <button type="button" className="saved__button saved__add" onClick={startNaming}>
          <span aria-hidden="true">☆</span> Save this location
        </button>
      )}

      {full && (
        <p className="saved__note">
          {MAX_SAVED_LOCATIONS} saved: delete one to save another, or reuse a name to replace that one.
        </p>
      )}
      {error && (
        <p role="alert" className="saved__error">
          {describe(error)}
        </p>
      )}
    </section>
  )
}
