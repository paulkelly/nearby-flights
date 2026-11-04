import { useRegisterSW } from 'virtual:pwa-register/react'

// How often an open app asks the server whether a new version exists. Browsers also check on every page load,
// but an installed app (or a tab left open all day) might not reload for a long time.
const UPDATE_CHECK_INTERVAL_MS = 60 * 60 * 1000

/**
 * Registers the service worker and shows a small banner when a new version of the app has been downloaded.
 *
 * Why ask instead of updating automatically: the new service worker waits until the user agrees, then takes over
 * and the page reloads. Swapping code silently mid-session could leave the open page running old JavaScript
 * against new cached files.
 */
export function UpdatePrompt() {
  const {
    needRefresh: [needRefresh, setNeedRefresh],
    updateServiceWorker,
  } = useRegisterSW({
    onRegisteredSW(_url, registration) {
      if (!registration) return
      setInterval(() => void registration.update(), UPDATE_CHECK_INTERVAL_MS)
    },
  })

  if (!needRefresh) return null

  return (
    <div role="status" className="update-prompt">
      <span>A new version is available.</span>
      <button type="button" onClick={() => void updateServiceWorker(true)}>
        Reload
      </button>
      <button type="button" className="update-prompt__dismiss" onClick={() => setNeedRefresh(false)}>
        Later
      </button>
    </div>
  )
}
