package io.github.paulkelly.flightradar.widget

import io.github.paulkelly.flightradar.data.Airport
import io.github.paulkelly.flightradar.data.Flight
import io.github.paulkelly.flightradar.data.FlightsResponse
import io.github.paulkelly.flightradar.data.SavedLocation
import java.text.NumberFormat
import kotlin.math.abs
import kotlin.math.roundToInt
import kotlinx.serialization.Serializable

// What a widget remembers, and the pure logic behind it. Each widget on the home screen has its own copy (two
// widgets can watch two places). Stored as JSON in the widget's Glance state (see FlightWidgetState).

/** The saved location a widget watches: a copy, so the widget keeps working offline and if the app's list changes. */
@Serializable
data class WidgetLocation(
    val id: String,
    val name: String,
    val lat: Double,
    val lon: Double,
    val radiusKm: Double,
) {
    companion object {
        fun from(location: SavedLocation) =
            WidgetLocation(location.id, location.name, location.lat, location.lon, location.radiusKm)
    }
}

/**
 * The aircraft a widget shows, already reduced to what its card needs: the same parts as the web app's nearest card.
 * Fields added after the first version have defaults, so a snapshot stored by an older version still loads.
 */
@Serializable
data class WidgetAircraft(
    /** The heading: airline name, else callsign, else registration, else the transponder's hex code. Never blank. */
    val title: String,
    /** Under an airline heading: the flight's own name (callsign, else registration, else hex). Else null. */
    val name: String? = null,
    /** The airline's ICAO code ("RYR"), when known: its logo is [FlightWidgetUiState.logo], else a badge. */
    val airlineCode: String? = null,
    /** "DUB → STN", "From DUB", or null when unknown (narrow widgets). */
    val route: String? = null,
    /** "Dublin (DUB) → London (STN)", "From Dublin (DUB)", or null when unknown (wide widgets). */
    val routeLong: String? = null,
    val aircraftType: String? = null,
    val distanceKm: Double,
    val altitudeFt: Int? = null,
    val onGround: Boolean,
    val groundSpeedKt: Double? = null,
    val trackDeg: Double? = null,
    val verticalRateFpm: Int? = null,
)

/** The result of the latest refresh. */
@Serializable
data class WidgetSnapshot(
    /** The aircraft to show: the nearest in the air, else the nearest on the ground, else null (none in range). */
    val aircraft: WidgetAircraft?,
    /** All aircraft in range, in the air or not. */
    val count: Int,
    /** When this data was fetched (device clock, ms since 1970). */
    val updatedAtMs: Long,
)

/**
 * The aircraft for the widget. `flights` come nearest first from the API. Like the web app's nearest card, prefer one
 * in the air (at an airport the nearest is usually parked at a gate), else show the nearest on the ground rather
 * than nothing. No hysteresis: the widget updates every few minutes, not every second, so there's no flicker to stop.
 */
fun chooseForWidget(flights: List<Flight>): Flight? = flights.firstOrNull { !it.onGround } ?: flights.firstOrNull()

fun snapshotOf(response: FlightsResponse, nowMs: Long) = WidgetSnapshot(
    aircraft = chooseForWidget(response.flights)?.let(::toWidgetAircraft),
    count = response.flights.size,
    updatedAtMs = nowMs,
)

fun toWidgetAircraft(flight: Flight): WidgetAircraft {
    // The web's displayName: callsign, else registration, else the hex code.
    val name = flight.callsign?.takeIf { it.isNotBlank() } ?: flight.registration ?: flight.hex.uppercase()
    return WidgetAircraft(
        title = flight.airline ?: name,
        name = if (flight.airline != null) name else null,
        airlineCode = flight.airlineCode,
        route = routeShort(flight),
        routeLong = routeLong(flight),
        aircraftType = flight.aircraftType,
        distanceKm = flight.distanceKm,
        altitudeFt = flight.altitudeFt,
        onGround = flight.onGround,
        groundSpeedKt = flight.groundSpeedKt,
        trackDeg = flight.trackDeg,
        verticalRateFpm = flight.verticalRateFpm,
    )
}

/** The web app's short route line (`routeLine(flight, 'short')`): "DUB → STN", "From DUB". */
fun routeShort(flight: Flight): String? = routeText(flight) { airportCode(it) }

/** The web app's long route line (`routeLine(flight, 'long')`): "Dublin (DUB) → London (STN)", "From Dublin (DUB)". */
fun routeLong(flight: Flight): String? = routeText(flight) { "${it.city ?: it.name} (${airportCode(it)})" }

/** The route if one is known, else the guess ("From …", "To …", "At …"), else null; each airport as [airport] says. */
private fun routeText(flight: Flight, airport: (Airport) -> String): String? {
    flight.route?.airports?.takeIf { it.isNotEmpty() }?.let { airports ->
        return airports.joinToString(" → ", transform = airport)
    }
    return flight.airportGuess?.let { "${it.kind} ${airport(it.airport)}" }
}

/** IATA ("DUB") where there is one, else ICAO ("EIDW"). */
private fun airportCode(airport: Airport) = airport.iata ?: airport.icao

// ---- Text, as the web app writes it (web/src/format.ts) ----

/** Climbing or descending slower than this counts as level (the web's LEVEL_FLIGHT_FPM). */
private const val LEVEL_FLIGHT_FPM = 100

/** "3.4 km", "27.0 km": one decimal, like the web's card. */
fun formatDistance(km: Double): String = "${oneDecimal().format(km)} km"

/** A search radius: "10 km" (tidy values are whole numbers), "2.5 km". */
fun formatRadius(km: Double): String =
    if (km % 1.0 == 0.0) "${km.roundToInt()} km" else "${oneDecimal().format(km)} km"

/** "36,000 ft", "On ground" or "Unknown". */
fun formatAltitude(aircraft: WidgetAircraft): String = when {
    aircraft.onGround -> "On ground"
    aircraft.altitudeFt == null -> "Unknown"
    else -> "${NumberFormat.getIntegerInstance().format(aircraft.altitudeFt)} ft"
}

/** "412 kt" or "Unknown". */
fun formatSpeed(knots: Double?): String = if (knots == null) "Unknown" else "${knots.roundToInt()} kt"

/** "275°" or "Unknown". */
fun formatTrack(degrees: Double?): String = if (degrees == null) "Unknown" else "${degrees.roundToInt()}°"

/**
 * "↑ 1,500 ft/min" (climbing), "↓ 800 ft/min" (descending), "Level" or "Unknown". Arrows instead of the web's
 * "Climbing"/"Descending": a widget's columns are narrow, and the words pushed the value into "…".
 */
fun formatVerticalRate(feetPerMinute: Int?): String = when {
    feetPerMinute == null -> "Unknown"
    abs(feetPerMinute) < LEVEL_FLIGHT_FPM -> "Level"
    else -> {
        val arrow = if (feetPerMinute > 0) "↑" else "↓"
        "$arrow ${NumberFormat.getIntegerInstance().format(abs(feetPerMinute))} ft/min"
    }
}

/**
 * The hue (0–359) of an airline's fallback badge, the same as the web's `badgeHue`: a small string hash, so the same
 * code always gets the same colour, in the app and on the website.
 */
fun badgeHue(code: String): Int = code.fold(0) { hash, char -> (hash * 31 + char.code) % 360 }

/** Formats with exactly one decimal place, in the phone's language ("3.4" in English, "3,4" in German). */
private fun oneDecimal() = NumberFormat.getNumberInstance().apply {
    minimumFractionDigits = 1
    maximumFractionDigits = 1
}
