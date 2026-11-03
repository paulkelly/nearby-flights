import { useState } from 'react'
import type { Flight, LatLon } from './api/flights'
import { useFlights } from './api/useFlights'
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

  return (
    <div className="layout">
      <aside className="sidebar">
        <h1>Nearby Flights</h1>
        <p className="hint">Click the map to choose a centre.</p>
        <RadiusControl radiusKm={radiusKm} onChange={setRadiusKm} />

        <p className="status">
          {isPending ? 'Loading…' : `${data?.count ?? 0} aircraft`}
          {isFetching && !isPending && ' · refreshing…'}
        </p>
        {error && <p role="alert" className="error">Couldn't load flights: {error.message}</p>}

        {/* Temporary list; piece 5 replaces it with a proper, selectable one. */}
        <ul className="flight-list">
          {data?.flights.map((flight) => (
            <li key={flight.hex}>
              {flight.callsign ?? flight.hex} · {flight.aircraftType ?? '?'} · {flight.distanceKm} km
            </li>
          ))}
        </ul>
      </aside>

      <FlightMap centre={centre} radiusKm={radiusKm} flights={data?.flights ?? NO_FLIGHTS} onCentreChange={setCentre} />
    </div>
  )
}

export default App
