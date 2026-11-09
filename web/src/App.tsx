import { useMemo, useState } from 'react'
import type { LatLon } from './api/flights'
import { useFlights } from './api/useFlights'
import { useNearestFlight } from './flights/useNearestFlight'
import { RECHECK_INTERVAL_MS, useVisibleFlights } from './flights/useVisibleFlights'
import { listedFlights } from './flights/visibleFlights'
import { FlightList } from './components/FlightList'
import { FlightMap, type ViewRequest } from './components/FlightMap'
import { LocateButton } from './components/LocateButton'
import { NearestFlightCard } from './components/NearestFlightCard'
import { RadiusControl } from './components/RadiusControl'
import { UpdatePrompt } from './components/UpdatePrompt'
import { positionAgeMs } from './geo/deadReckoning'
import { roundLatLon } from './geo/geo'
import { useDebouncedValue } from './hooks/useDebouncedValue'
import { useGeolocation } from './hooks/useGeolocation'
import { useNow } from './hooks/useNow'
import { useStoredBoolean } from './hooks/useStoredBoolean'
import './App.css'

const DUBLIN_AIRPORT: LatLon = { lat: 53.4213, lon: -6.2701 }
const DEFAULT_RADIUS_KM = 10
const SLIDER_DEBOUNCE_MS = 300

function App() {
  // App owns the search state; children get values and callbacks as props and report changes back up.
  const [centre, setCentre] = useState(DUBLIN_AIRPORT)
  const [radiusKm, setRadiusKm] = useState(DEFAULT_RADIUS_KM)

  // The circle follows the slider instantly; the request waits until the slider settles.
  const queryRadiusKm = useDebouncedValue(radiusKm, SLIDER_DEBOUNCE_MS)
  const { data, error, isPending, isFetching, isPlaceholderData, isRateLimited } = useFlights(centre, queryRadiusKm)

  // What to show: the aircraft we have (fresh or cached) re-measured from the *current* centre and radius, using
  // their predicted positions. That keeps the display right while a new search loads, or when it can't load at
  // all (offline), and makes the slider show/hide planes instantly instead of after the debounced request.
  // One clock for both, so the list and the nearest card always agree. Like a fixed-rate tick in a game loop.
  const now = useNow(RECHECK_INTERVAL_MS)
  const visible = useVisibleFlights(data, centre, radiusKm, now)
  const nearest = useNearestFlight(visible, now)
  // How old the nearest aircraft's report is, so the card can dead-reckon its altitude between polls.
  const nearestAgeMs =
    nearest && data ? positionAgeMs(nearest.flight.positionTimestamp, data.fetchedAt, data.receivedAtMs, now) : 0
  const visibleOnMap = useMemo(() => visible.map(({ flight }) => flight), [visible])

  // The list leaves out aircraft on the ground unless asked: at an airport they'd fill it with parked planes.
  // Only the list: the map still shows them (greyed), and the nearest card has its own rules.
  // Remembered across visits in this browser: it's a preference, not part of a search.
  const [showGrounded, setShowGrounded] = useStoredBoolean('nearby-flights:show-grounded', false)
  const groundedCount = useMemo(() => visible.filter(({ flight }) => flight.onGround).length, [visible])
  const listed = useMemo(() => listedFlights(visible, showGrounded), [visible, showGrounded])

  // Data from another area or an older poll that couldn't be refreshed: say so rather than present it as live.
  const showingStaleData = Boolean(data) && (isPlaceholderData || Boolean(error) || isRateLimited)

  // "Use my location": search around the device's position and move the map there, since it may be off-screen.
  const [viewRequest, setViewRequest] = useState<ViewRequest | null>(null)
  const geolocation = useGeolocation((position) => {
    const rounded = roundLatLon(position)
    setCentre(rounded)
    setViewRequest({ centre: rounded, radiusKm })
  })

  // One selection shared by list and map, so they can never disagree. Stored as the hex ID, not the Flight
  // object: each poll brings fresh Flight objects, but the hex stays the same.
  const [selectedHex, setSelectedHex] = useState<string | null>(null)

  // A popup closing clears the selection, but only if it still belongs to that aircraft. When you pick plane B
  // while A's popup is open, Leaflet closes A's popup *after* B is selected; the functional update (which sees
  // the latest state) makes sure that late "A closed" event doesn't wipe out the selection of B.
  const deselect = (hex: string) => setSelectedHex((current) => (current === hex ? null : current))

  // Hover works the same way: list rows and map markers both set it, both highlight from it. The same
  // "only clear if it's still mine" guard covers moving straight from one plane to another, where the new
  // plane's mouseover can arrive before the old one's mouseout.
  const [hoveredHex, setHoveredHex] = useState<string | null>(null)
  const unhover = (hex: string) => setHoveredHex((current) => (current === hex ? null : current))

  // The nearest card is different: its aircraft can change while the pointer rests on it, and no mouseleave
  // fires, so storing "hovering plane A" would leave A highlighted after the card moved on to B. Instead store
  // only *whether* the card is hovered, and derive the aircraft on every render: derived values can't go stale.
  const [nearestCardHovered, setNearestCardHovered] = useState(false)
  const highlightedHex = nearestCardHovered && nearest ? nearest.flight.hex : hoveredHex

  // On phones the sidebar is a bottom sheet that is collapsed (controls only) or expanded (with the list).
  // On wide screens the CSS ignores this flag and always shows everything, so no JavaScript screen-size checks.
  const [sheetExpanded, setSheetExpanded] = useState(false)

  // Picking an aircraft in the list or the card again unselects it (and closes its popup), like clicking its
  // marker again. Picking one collapses the sheet, so on a phone the map (and the plane's popup) is visible.
  const selectFromList = (hex: string) => {
    if (hex === selectedHex) {
      setSelectedHex(null)
      return
    }
    setSelectedHex(hex)
    setSheetExpanded(false)
  }

  const statusText = isPending
    ? 'Loading…'
    : `${visible.length} aircraft${showingStaleData ? ' · last known positions' : isFetching ? ' · refreshing…' : ''}`

  return (
    <div className="layout">
      <aside className="sidebar" data-expanded={sheetExpanded}>
        {/* Phone-only handle (hidden by CSS on wide screens): shows the count and toggles the list. */}
        <button
          type="button"
          className="sheet-handle"
          aria-expanded={sheetExpanded}
          aria-controls="flight-list"
          onClick={() => setSheetExpanded((expanded) => !expanded)}
        >
          <span className="sheet-handle__grip" aria-hidden="true" />
          <span>{statusText}</span>
          <span className="sheet-handle__action">{sheetExpanded ? 'Hide list' : 'Show list'}</span>
        </button>

        <h1>Nearby Flights</h1>
        {/* Both hints are rendered; CSS shows the one matching the device's main pointer (mouse vs finger). */}
        <p className="hint">
          <span className="hint--mouse">Click the map to choose a centre.</span>
          <span className="hint--touch">Long-press the map to search there.</span>
        </p>
        <RadiusControl radiusKm={radiusKm} onChange={setRadiusKm} />
        <LocateButton state={geolocation.state} onLocate={geolocation.locate} />
        <p className="status">{statusText}</p>

        {/* Outside the sheet body, so phones show it even with the list collapsed. */}
        {!isPending && (
          <NearestFlightCard
            nearest={nearest}
            ageMs={nearestAgeMs}
            selectedHex={selectedHex}
            hoveredHex={highlightedHex}
            onSelect={selectFromList}
            onHoverChange={setNearestCardHovered}
          />
        )}

        {/* Rate-limited is its own message: it isn't a failure, and it outlasts `error` (moving the map while
            paused starts a new query with no error yet, but fetching is still paused). */}
        {isRateLimited ? (
          <p role="status" className="error">
            Too many requests: pausing updates for a moment.
          </p>
        ) : (
          error && (
            <p role="alert" className="error">
              Couldn’t refresh: {error.message}
            </p>
          )
        )}

        <div id="flight-list" className="sheet-body">
          <label className="grounded-toggle">
            <input type="checkbox" checked={showGrounded} onChange={(event) => setShowGrounded(event.target.checked)} />
            Show aircraft on the ground{groundedCount > 0 && ` (${groundedCount})`}
          </label>
          {!isPending && (
            <FlightList
              flights={listed}
              emptyMessage={
                groundedCount > 0 && !showGrounded
                  ? 'Only aircraft on the ground here right now.'
                  : 'No aircraft in this area right now.'
              }
              selectedHex={selectedHex}
              hoveredHex={highlightedHex}
              onSelect={selectFromList}
              onHover={setHoveredHex}
              onUnhover={unhover}
            />
          )}
        </div>
      </aside>

      <FlightMap
        centre={centre}
        radiusKm={radiusKm}
        viewRequest={viewRequest}
        flights={visibleOnMap}
        trackedFlight={nearest?.flight ?? null}
        fetchedAt={data?.fetchedAt ?? ''}
        receivedAtMs={data?.receivedAtMs ?? 0}
        selectedHex={selectedHex}
        hoveredHex={highlightedHex}
        onCentreChange={setCentre}
        onSelect={setSelectedHex}
        onDeselect={deselect}
        onHover={setHoveredHex}
        onUnhover={unhover}
      />

      <UpdatePrompt />
    </div>
  )
}

export default App
