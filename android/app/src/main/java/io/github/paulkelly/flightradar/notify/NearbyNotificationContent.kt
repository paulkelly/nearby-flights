package io.github.paulkelly.flightradar.notify

import io.github.paulkelly.flightradar.widget.WidgetAircraft
import io.github.paulkelly.flightradar.widget.WidgetSnapshot
import io.github.paulkelly.flightradar.widget.formatAltitude
import io.github.paulkelly.flightradar.widget.formatDistance
import io.github.paulkelly.flightradar.widget.isClimbingOrDescending
import java.text.NumberFormat

/**
 * What the lock-screen notification says (step 9e). Android lays it out: [header] in the small top line (next to the
 * app's name and the time), then [title] in bold, then [text].
 *
 * ```
 * ✈ Flight Radar · Home · 3 aircraft · 14:05
 * Aer Lingus EIN123 · 4.2 km
 * DUB → LHR · 12,000 ft ↑ · A320
 * ```
 */
data class NearbyNotificationContent(val header: String, val title: String, val text: String)

/**
 * The notification for [snapshot] at [locationName], or null when there's nothing to notify about: nothing near, or
 * only aircraft on the ground (a plane parked at a nearby airport would otherwise sit on the lock screen all day).
 * The widget still shows those; the notification is for aircraft overhead.
 */
fun nearbyNotificationContent(locationName: String, snapshot: WidgetSnapshot): NearbyNotificationContent? {
    val aircraft = snapshot.aircraft?.takeUnless { it.onGround } ?: return null
    val count = if (snapshot.count > 1) "${NumberFormat.getIntegerInstance().format(snapshot.count)} aircraft" else null
    return NearbyNotificationContent(
        header = listOfNotNull(locationName, count).joinToString(" · "),
        title = "${listOfNotNull(aircraft.title, aircraft.name).joinToString(" ")} · ${formatDistance(aircraft.distanceKm)}",
        text = listOfNotNull(aircraft.route, altitudeWithTrend(aircraft), aircraft.aircraftType).joinToString(" · "),
    )
}

/** "12,000 ft ↑", "12,000 ft ↓", "36,000 ft" (level, or rate unknown), or null when the altitude is unknown. */
private fun altitudeWithTrend(aircraft: WidgetAircraft): String? {
    if (aircraft.altitudeFt == null) return null
    val rate = aircraft.verticalRateFpm
    val arrow = when {
        rate == null || !isClimbingOrDescending(rate) -> null
        rate > 0 -> "↑"
        else -> "↓"
    }
    return listOfNotNull(formatAltitude(aircraft), arrow).joinToString(" ")
}
