import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ReactQueryDevtools } from '@tanstack/react-query-devtools'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// Leaflet's own stylesheet: positions tiles, controls and popups. Without it the map renders as scattered tiles.
import 'leaflet/dist/leaflet.css'
import './index.css'
import App from './App.tsx'

// One cache for the whole app. The provider makes it available to every component below it,
// a bit like a service registered in DI and injected wherever useQuery is called.
const queryClient = new QueryClient()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <App />
      {/* Floating panel showing the query cache. Automatically left out of production builds. */}
      <ReactQueryDevtools buttonPosition="bottom-left" />
    </QueryClientProvider>
  </StrictMode>,
)
