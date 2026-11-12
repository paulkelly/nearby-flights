package io.github.paulkelly.flightradar.widget

import io.github.paulkelly.flightradar.data.Airport
import io.github.paulkelly.flightradar.data.Flight
import io.github.paulkelly.flightradar.data.FlightsResponse
import io.github.paulkelly.flightradar.data.SavedLocation
import java.text.NumberFormat
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

/** The aircraft a widget shows, already reduced to what fits on it. */
@Serializable
data class WidgetAircraft(
    /** Airline name, else callsign, else registration, else the transponder's hex code: never blank. */
    val title: String,
    /** The callsign when the title is the airline ("RYR2DQ"), else null (it would repeat the title). */
    val callsign: String?,
    /** The airline's ICAO code ("RYR"), when known: its logo is [FlightWidgetUiState.logo]. */
    val airlineCode: String? = null,
    /** "DUB → STN", "From DUB", "To DUB", "At DUB", or null when unknown. */
    val route: String?,
    val aircraftType: String?,
    val distanceKm: Double,
    val altitudeFt: Int?,
    val onGround: Boolean,
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
    val callsign = flight.callsign?.takeIf { it.isNotBlank() }
    val title = flight.airline ?: callsign ?: flight.registration ?: flight.hex.uppercase()
    return WidgetAircraft(
        title = title,
        callsign = if (flight.airline != null) callsign else null,
        airlineCode = flight.airlineCode,
        route = routeShort(flight),
        aircraftType = flight.aircraftType,
        distanceKm = flight.distanceKm,
        altitudeFt = flight.altitudeFt,
        onGround = flight.onGround,
    )
}

/** The web app's short route line (`routeLine(flight, 'short')`): codes only, to fit a widget. */
fun routeShort(flight: Flight): String? {
    flight.route?.airports?.takeIf { it.isNotEmpty() }?.let { airports ->
        return airports.joinToString(" → ") { airportCode(it) }
    }
    return flight.airportGuess?.let { "${it.kind} ${airportCode(it.airport)}" }
}

/** IATA ("DUB") where there is one, else ICAO ("EIDW"). */
private fun airportCode(airport: Airport) = airport.iata ?: airport.icao

// ---- Text helpers for the widget's layout ----

/** "3.4 km" under 10 km, "27 km" above. */
fun formatDistance(km: Double): String =
    if (km < 10) "${(km * 10).roundToInt() / 10.0} km" else "${km.roundToInt()} km"

/** "36,000 ft", "On the ground", or null when the altitude is unknown. */
fun formatAltitude(aircraft: WidgetAircraft): String? = when {
    aircraft.onGround -> "On the ground"
    aircraft.altitudeFt == null -> null
    else -> "${NumberFormat.getIntegerInstance().format(aircraft.altitudeFt)} ft"
}
