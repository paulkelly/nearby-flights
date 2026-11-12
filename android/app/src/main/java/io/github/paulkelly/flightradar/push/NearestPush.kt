package io.github.paulkelly.flightradar.push

import io.github.paulkelly.flightradar.data.Flight
import io.github.paulkelly.flightradar.data.FlightRadarApi
import io.github.paulkelly.flightradar.data.parseServerTime
import io.github.paulkelly.flightradar.widget.WidgetSnapshot
import io.github.paulkelly.flightradar.widget.toWidgetAircraft

/**
 * A "nearest" push from the server (step 9d): the aircraft a location's widget should show now, already chosen by the
 * server (with hysteresis), or none. Pure data, read from FCM's string map by [parse].
 */
data class NearestPush(
    val locationId: String,
    /** When the server fetched this (its clock, ms since 1970). */
    val fetchedAtMs: Long,
    /** The aircraft, or null: nothing near ("clear skies"), or [flightOmitted]. */
    val flight: Flight?,
    /** The aircraft didn't fit in a push (FCM's 4 KB): fetch instead. */
    val flightOmitted: Boolean,
    /** Aircraft in the circle, in the air or not. */
    val count: Int,
) {
    /** What the widget stores. The server's fetch time, so pushes and refreshes compare on one clock (near enough). */
    fun toSnapshot() = WidgetSnapshot(aircraft = flight?.let(::toWidgetAircraft), count = count, updatedAtMs = fetchedAtMs)

    companion object {
        const val TYPE = "nearest"

        /** The push, or null if it isn't a "nearest" push or can't be read (a newer server's shape, say). */
        fun parse(data: Map<String, String>): NearestPush? {
            if (data["type"] != TYPE) return null
            val locationId = data["locationId"]?.takeIf { it.isNotBlank() } ?: return null
            val fetchedAtMs = parseServerTime(data["fetchedAt"]) ?: return null
            val flight = data["flight"]?.let {
                try {
                    FlightRadarApi.json.decodeFromString<Flight>(it)
                } catch (_: IllegalArgumentException) { // SerializationException is one
                    return null
                }
            }
            return NearestPush(
                locationId = locationId,
                fetchedAtMs = fetchedAtMs,
                flight = flight,
                flightOmitted = data["flightOmitted"] == "true",
                count = data["count"]?.toIntOrNull() ?: if (flight == null) 0 else 1,
            )
        }

        /**
         * Whether a push should replace what the widget shows: only if it's newer. Pushes can arrive late or out of
         * order (and after a refresh the widget did itself), and older news must not overwrite newer.
         */
        fun isNewer(push: NearestPush, current: WidgetSnapshot?): Boolean =
            current == null || push.fetchedAtMs > current.updatedAtMs
    }
}
