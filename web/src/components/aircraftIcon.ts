import { divIcon, type DivIcon } from 'leaflet'

// A top-down plane silhouette pointing up (north), in a 24×24 box.
const PLANE_PATH =
  'M12 2 L13.5 9 L21 13 L21 15 L13.5 12.5 L13 18 L15.5 20 L15.5 21.5 L12 20.5 ' +
  'L8.5 21.5 L8.5 20 L11 18 L10.5 12.5 L3 15 L3 13 L10.5 9 Z'

const ICON_SIZE = 48

// Icons are cached per (rounded heading, ground state). Re-renders then hand Leaflet the *same* icon object,
// so it leaves the marker's DOM alone instead of rebuilding it every 10 s poll. At most 360 × 2 icons exist.
const cache = new Map<string, DivIcon>()

/**
 * A plane icon rotated to `trackDeg` (clockwise from north, which matches CSS rotate()).
 * Aircraft with no known track are drawn pointing north.
 */
export function aircraftIcon(trackDeg: number | null, onGround: boolean): DivIcon {
  const heading = Math.round(trackDeg ?? 0) % 360
  const key = `${heading}|${onGround}`

  let icon = cache.get(key)
  if (!icon) {
    icon = divIcon({
      // Replaces Leaflet's default "leaflet-div-icon" class, which draws a white box behind the icon.
      className: onGround ? 'aircraft-icon aircraft-icon--ground' : 'aircraft-icon',
      iconSize: [ICON_SIZE, ICON_SIZE],
      iconAnchor: [ICON_SIZE / 2, ICON_SIZE / 2], // the marker's position is the icon's centre
      // The rotation goes on an inner element: Leaflet positions the outer element with its own CSS transform,
      // and a rotate() there would overwrite it, piling every marker into the map's top-left corner.
      // Only numbers go into this raw HTML. Never put API strings (callsigns) here: that's an injection risk.
      html:
        `<svg viewBox="0 0 24 24" width="${ICON_SIZE}" height="${ICON_SIZE}" ` +
        `style="transform: rotate(${heading}deg)"><path d="${PLANE_PATH}"/></svg>`,
    })
    cache.set(key, icon)
  }
  return icon
}
