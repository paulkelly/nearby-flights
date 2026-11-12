package io.github.paulkelly.flightradar.notify

import io.github.paulkelly.flightradar.widget.WidgetSnapshot

/**
 * What the lock-screen notification says (step 9e): only who it is and where it's going, nothing more (no distance,
 * altitude or type: the widget has those). Android lays it out: [header] in the small top line (next to the app's
 * name and the time), [title] in bold, then [text]; the airline's logo is the large icon, on the right.
 *
 * ```
 * ✈ Flight Radar · Home · 14:05
 * easyJet EZY151G                      [logo]
 * LGW → BHD
 * ```
 */
data class NearbyNotificationContent(val header: String, val title: String, val text: String?)

/**
 * The notification for [snapshot] at [locationName], or null when there's nothing to notify about: nothing near, or
 * only aircraft on the ground (a plane parked at a nearby airport would otherwise sit on the lock screen all day).
 * The widget still shows those; the notification is for aircraft overhead.
 */
fun nearbyNotificationContent(locationName: String, snapshot: WidgetSnapshot): NearbyNotificationContent? {
    val aircraft = snapshot.aircraft?.takeUnless { it.onGround } ?: return null
    return NearbyNotificationContent(
        header = locationName,
        title = listOfNotNull(aircraft.title, aircraft.name).joinToString(" "), // "easyJet EZY151G", or "EI-ABC"
        text = aircraft.route, // "LGW → BHD", "From DUB", or none when unknown
    )
}
