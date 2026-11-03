import { useState } from 'react'
import type { Flight, LatLon } from './api/flights'
import { useFlights } from './api/useFlights'
import { FlightList } from './components/FlightList'
import { FlightMap } from './components/FlightMap'
import { RadiusControl } from './components/RadiusControl'
import { useDebouncedValue } from './hooks/useDebouncedValue'
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

  return (
    <div className="layout">
      <aside className="sidebar">
        <h1>Nearby Flights</h1>
        <p className="hint">Click the map to choose a centre.</p>
        <RadiusControl radiusKm={radiusKm} onChange={setRadiusKm} />

        <p className="status">
          {isPending ? 'Loading…' : `${flights.length} aircraft`}
          {isFetching && !isPending && ' · refreshing…'}
        </p>

        {error && (
          <p role="alert" className="error">
            {error.message}
            {/* TanStack Query keeps the last successful data when a refetch fails. */}
            {data && ' · showing last known positions'}
          </p>
        )}

        {!isPending && (
          <FlightList
            flights={flights}
            selectedHex={selectedHex}
            hoveredHex={hoveredHex}
            onSelect={setSelectedHex}
            onHover={setHoveredHex}
            onUnhover={unhover}
          />
        )}
      </aside>

      <FlightMap
        centre={centre}
        radiusKm={radiusKm}
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
    </div>
  )
}

export default App
