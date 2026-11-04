import { useState } from 'react'
import type { Flight, LatLon } from './api/flights'
import { useFlights } from './api/useFlights'
import { FlightList } from './components/FlightList'
import { FlightMap, type ViewRequest } from './components/FlightMap'
import { LocateButton } from './components/LocateButton'
import { RadiusControl } from './components/RadiusControl'
import { UpdatePrompt } from './components/UpdatePrompt'
import { roundLatLon } from './geo/geo'
import { useDebouncedValue } from './hooks/useDebouncedValue'
import { useGeolocation } from './hooks/useGeolocation'
import './App.css'

const DUBLIN_AIRPORT: LatLon = { lat: 53.4213, lon: -6.2701 }
const DEFAULT_RADIUS_KM = 50
const SLIDER_DEBOUNCE_MS = 300
// A single shared empty array. `?? []` would make a new array on every render; harmless today, but anything
// that compares by reference (useMemo/useEffect dependencies, memoised components) would see "new data" each time.
const NO_FLIGHTS: Flight[] = []

function App() {
  // App owns the search state; children get values and callbacks as props and report changes back up.
  const [centre, setCentre] = useState(DUBLIN_AIRPORT)
  const [radiusKm, setRadiusKm] = useState(DEFAULT_RADIUS_KM)

  // The circle follows the slider instantly; the request waits until the slider settles.
  const queryRadiusKm = useDebouncedValue(radiusKm, SLIDER_DEBOUNCE_MS)
  const { data, error, isPending, isFetching } = useFlights(centre, queryRadiusKm)
  const flights = data?.flights ?? NO_FLIGHTS

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

  // On phones the sidebar is a bottom sheet that is collapsed (controls only) or expanded (with the list).
  // On wide screens the CSS ignores this flag and always shows everything, so no JavaScript screen-size checks.
  const [sheetExpanded, setSheetExpanded] = useState(false)

  // Picking a flight from the list collapses the sheet, so on a phone the map (and the plane's popup) is visible.
  const selectFromList = (hex: string) => {
    setSelectedHex(hex)
    setSheetExpanded(false)
  }

  const statusText = isPending ? 'Loading…' : `${flights.length} aircraft${isFetching ? ' · refreshing…' : ''}`

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

        {error && (
          <p role="alert" className="error">
            {error.message}
            {/* TanStack Query keeps the last successful data when a refetch fails. */}
            {data && ' · showing last known positions'}
          </p>
        )}

        <div id="flight-list" className="sheet-body">
          {!isPending && (
            <FlightList
              flights={flights}
              selectedHex={selectedHex}
              hoveredHex={hoveredHex}
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
        flights={flights}
        fetchedAt={data?.fetchedAt ?? ''}
        receivedAtMs={data?.receivedAtMs ?? 0}
        selectedHex={selectedHex}
        hoveredHex={hoveredHex}
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
