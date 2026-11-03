import { useFlights } from './api/useFlights'

// Temporary: a plain list proving the data flow works end to end. The map replaces this in the next piece.
const DUBLIN_AIRPORT = { lat: 53.4213, lon: -6.2701 }
const RADIUS_KM = 30

function App() {
  const { data, error, isPending, isFetching } = useFlights(DUBLIN_AIRPORT, RADIUS_KM)

  return (
    <main style={{ padding: '1rem' }}>
      <h1>Nearby Flights</h1>
      <p>
        Within {RADIUS_KM} km of Dublin Airport {isFetching && '(refreshing…)'}
      </p>

      {isPending && <p>Loading…</p>}
      {error && <p role="alert">Couldn't load flights: {error.message}</p>}

      {data && (
        <ul>
          {data.flights.map((flight) => (
            // key: React needs a stable ID per list item to match items between renders. Hex never changes.
            <li key={flight.hex}>
              {flight.callsign ?? flight.hex} · {flight.aircraftType ?? '?'} · {flight.distanceKm} km ·{' '}
              {flight.onGround ? 'on ground' : `${flight.altitudeFt ?? '?'} ft`}
            </li>
          ))}
        </ul>
      )}
    </main>
  )
}

export default App
