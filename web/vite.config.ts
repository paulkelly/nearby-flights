import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    // Generates dist/sw.js, the service worker: a script the browser runs *alongside* the page, which can answer
    // the page's network requests itself. We use it to keep the app shell (HTML/JS/CSS/icons) on the device, so
    // the app opens instantly and even offline, and so Chrome treats the site as an installable app.
    VitePWA({
      // We ship our own public/manifest.webmanifest (step 4 piece 3) rather than having the plugin generate one.
      manifest: false,
      // Don't silently swap in a new version under the user's feet: the app shows a "reload" prompt
      // (UpdatePrompt.tsx) when a new build has been downloaded.
      registerType: 'prompt',
      // Registration happens in our own code via the plugin's React hook, not an injected <script>.
      injectRegister: false,
      workbox: {
        // Precache: every build file matching these patterns is downloaded on install and served from the
        // device afterwards. Vite's hashed file names mean a new build = a new precache list = an update.
        globPatterns: ['**/*.{js,css,html,svg,png,ico,webmanifest}'],
        // Page navigations (e.g. opening the installed app) get index.html from the precache, even offline...
        navigateFallback: '/index.html',
        // ...except API calls, which must always go to the network: live flight data is useless if stale.
        navigateFallbackDenylist: [/^\/api\//],
        // Deliberately no runtime caching: /api must be live, and OpenStreetMap's tile policy discourages apps
        // from bulk-caching tiles. Both just use the network (and the browser's normal HTTP cache).
      },
      // The service worker only runs in production builds. In `npm run dev` it would cache files that Vite
      // rebuilds on every save, making hot reload confusing.
      devOptions: { enabled: false },
    }),
  ],
  server: {
    // Forward /api/* to the ASP.NET Core API (its "http" launch profile). The browser only ever talks to the
    // Vite dev server, so requests are same-origin and the API needs no CORS setup. In production, nginx
    // plays the same role (roadmap step 3), so frontend code can always call a relative "/api/...".
    proxy: {
      '/api': 'http://localhost:5123',
    },
  },
})
