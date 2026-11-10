/**
 * Appended to the status line: "· ● Live" while pushes arrive over the WebSocket, "· updates every 10 s" while the
 * app is polling instead (not connected yet, connection lost, or pushes stopped).
 */
export function LiveStatus({ live }: { live: boolean }) {
  return live ? (
    <span className="live-status live-status--live" title="Updates arrive as soon as the server has them">
      {' · '}
      <span className="live-status__dot" aria-hidden="true">
        ●
      </span>{' '}
      Live
    </span>
  ) : (
    <span className="live-status" title="Not connected for live updates: asking for new data every 10 seconds">
      {' · updates every 10 s'}
    </span>
  )
}
