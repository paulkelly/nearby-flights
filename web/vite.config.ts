import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // Forward /api/* to the ASP.NET Core API (its "http" launch profile). The browser only ever talks to the
    // Vite dev server, so requests are same-origin and the API needs no CORS setup. In production, nginx
    // plays the same role (roadmap step 3), so frontend code can always call a relative "/api/...".
    proxy: {
      '/api': 'http://localhost:5123',
    },
  },
})
