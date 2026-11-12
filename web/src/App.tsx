import { useMemo, useState } from 'react'
import type { LatLon } from './api/flights'
import type { SavedLocation } from './api/locations'
import { useFlightStream } from './api/useFlightStream'
import { useFlights } from './api/useFlights'
import { useNearestFlight } from './flights/useNearestFlight'
import { RECHECK_INTERVAL_MS, useVisibleFlights } from './flights/useVisibleFlights'
import { listedFlights } from './flights/visibleFlights'
import { FlightList } from './components/FlightList'
import { FlightMap, type ViewRequest } from './components/FlightMap'
import { LiveStatus } from './components/LiveStatus'
import { LocateButton } from './components/LocateButton'
import { NearestFlightCard } from './components/NearestFlightCard'
import { RadiusControl } from './components/RadiusControl'
import { SavedLocations } from './components/SavedLocations'
import { UpdatePrompt } from './components/UpdatePrompt'
import { positionAgeMs } from './geo/deadReckoning'
import { roundLatLon } from './geo/geo'
import { useDebouncedValue } from './hooks/useDebouncedValue'
import { useGeolocation } from './hooks/useGeolocation'
import { useNow } from './hooks/useNow'
import { useStoredBoolean } from './hooks/useStoredBoolean'
import { useSavedLocations } from './locations/useSavedLocations'
import { useStartingPoint, type StartingPoint } from './locations/useStartingPoint'
import './App.css'

// Where the app starts when there's no saved location and no position to use (see useStartingPoint).
const DUBLIN_AIRPORT: LatLon = { lat: 53.4213, lon: -6.2701 }
const DEFAULT_RADIUS_KM = 10

type SheetTab = 'search' | 'aircraft'
const SLIDER_DEBOUNCE_MS = 300

function App() {
  // App owns the search state; children get values and callbacks as props and report changes back up.
  const [centre, setCentre] = useState(DUBLIN_AIRPORT)
  const [radiusKm, setRadiusKm] = useState(DEFAULT_RADIUS_KM)
  // Set to a new object to move the map (a search can be anywhere, including off-screen).
  const [viewRequest, setViewRequest] = useState<ViewRequest | null>(null)

  // On phones the sidebar is a bottom sheet: collapsed (just the nearest aircraft and the long-press hint) or
  // expanded, showing one of two tabs: the search controls, or the list of aircraft. The tab is remembered while
  // the sheet is collapsed. On wide screens the CSS ignores both and always shows everything, so no JavaScript
  // screen-size checks.
  const [sheetExpanded, setSheetExpanded] = useState(false)
  const [sheetTab, setSheetTab] = useState<SheetTab>('search')

  // Here rather than in <SavedLocations>, because the starting point is chosen from the same list.
  const savedLocations = useSavedLocations()

  // A saved location replaces the whole search, radius included, and brings the map there (it may be far away).
  const showSaved = (location: SavedLocation, instant = false) => {
    const savedCentre = { lat: location.lat, lon: location.lon }
    setCentre(savedCentre)
    setRadiusKm(location.radiusKm)
    setViewRequest({ centre: savedCentre, radiusKm: location.radiusKm, instant })
  }

  // The device's position: search around it and move the map there.
  const showPosition = (position: LatLon, instant = false) => {
    const rounded = roundLatLon(position)
    setCentre(rounded)
    setViewRequest({ centre: rounded, radiusKm, instant })
  }

  // Where to start: the first saved location, else the device's position, else Dublin (where the map already is).
  // Nothing is fetched until it's decided. Jumps rather than flies: there's no journey from Dublin to show.
  const start = useStartingPoint(savedLocations, (point: StartingPoint) => {
    if (point.kind === 'saved') showSaved(point.location, true)
    else if (point.kind === 'here') showPosition(point.position, true)
  })
  const starting = start.phase !== 'done'

  // Anything the user picks themselves ends the start-up: a position or list that arrives later mustn't move them.
  // Picking a saved location also collapses the sheet, showing the map on phones (as picking an aircraft does).
  const goToSaved = (location: SavedLocation) => {
    start.cancel()
    showSaved(location)
    setSheetExpanded(false)
  }
  const chooseCentre = (chosen: LatLon) => {
    start.cancel()
    setCentre(chosen)
  }
  // "Use my location"
  const geolocation = useGeolocation((position) => showPosition(position))
  const locate = () => {
    start.cancel()
    geolocation.locate()
  }

  // The circle follows the slider instantly; the request waits until the slider settles.
  const queryRadiusKm = useDebouncedValue(radiusKm, SLIDER_DEBOUNCE_MS)
  // Live pushes over a WebSocket when they're flowing (step 7b), else polling: both feed the same query cache.
  const { streamed } = useFlightStream(centre, queryRadiusKm, !starting)
  const { data, error, isPending, isFetching, isPlaceholderData, isRateLimited } = useFlights(
    centre,
    queryRadiusKm,
    streamed,
    !starting,
  )

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
  const [showGrounded, setShowGrounded] = useStoredBoolean('flight-radar:show-grounded', false)
  const groundedCount = useMemo(() => visible.filter(({ flight }) => flight.onGround).length, [visible])
  const listed = useMemo(() => listedFlights(visible, showGrounded), [visible, showGrounded])

  // Data from another area or an older poll that couldn't be refreshed: say so rather than present it as live.
  const showingStaleData = Boolean(data) && (isPlaceholderData || Boolean(error) || isRateLimited)

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

  const statusText =
    start.phase === 'locating'
      ? 'Finding your location…'
      : isPending
        ? 'Loading…'
        : `${visible.length} aircraft${showingStaleData ? ' · last known positions' : isFetching ? ' · refreshing…' : ''}`

  return (
    <div className="layout">
      <aside className="sidebar" data-expanded={sheetExpanded} data-tab={sheetTab}>
        {/* Phone-only handle (hidden by CSS on wide screens): shows the count and expands or collapses the sheet. */}
        <button
          type="button"
          className="sheet-handle"
          aria-expanded={sheetExpanded}
          onClick={() => setSheetExpanded((expanded) => !expanded)}
        >
          <span className="sheet-handle__grip" aria-hidden="true" />
          <span>
            {statusText}
            {!isPending && !showingStaleData && <LiveStatus live={streamed} />}
          </span>
          {/* The arrow points the way the sheet will move. */}
          <span className="sheet-handle__action">{sheetExpanded ? 'Less ▾' : 'More ▴'}</span>
        </button>

        {/* Phone-only tabs of the expanded sheet (hidden by CSS on wide screens and while collapsed). Toggle buttons
            (aria-pressed) rather than ARIA tabs: the "Search" side is several separate elements, not one panel. */}
        <div className="sheet-tabs" role="group" aria-label="Show">
          <button
            type="button"
            className="sheet-tabs__tab"
            aria-pressed={sheetTab === 'search'}
            onClick={() => setSheetTab('search')}
          >
            Search
          </button>
          <button
            type="button"
            className="sheet-tabs__tab"
            aria-pressed={sheetTab === 'aircraft'}
            onClick={() => setSheetTab('aircraft')}
          >
            Aircraft
          </button>
        </div>

        <h1>Flight Radar</h1>
        {/* Both hints are rendered; CSS shows the one matching the device's main pointer (mouse vs finger). */}
        <p className="hint">
          <span className="hint--mouse">Click the map to choose a centre.</span>
          <span className="hint--touch">Long-press the map to search there.</span>
        </p>
        <RadiusControl radiusKm={radiusKm} onChange={setRadiusKm} />
        <LocateButton state={geolocation.state} onLocate={locate} />
        <SavedLocations saved={savedLocations} centre={centre} radiusKm={radiusKm} onGo={goToSaved} />
        <p className="status">
          {statusText}
          {!isPending && !showingStaleData && <LiveStatus live={streamed} />}
        </p>

        {/* Outside the sheet body, so phones show it with the sheet collapsed (and on the Search tab). */}
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
        onCentreChange={chooseCentre}
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
