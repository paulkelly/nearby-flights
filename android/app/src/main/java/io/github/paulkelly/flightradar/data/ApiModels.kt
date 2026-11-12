package io.github.paulkelly.flightradar.data

import kotlinx.serialization.Serializable

// The API's JSON shapes (see the API's OpenAPI document). @Serializable makes the compiler plugin generate each
// class's serializer at build time. Property names match the JSON's camelCase, so no renaming is needed.

/** `POST /api/owners`: a new anonymous owner. The token is shown once; only its hash is stored on the server. */
@Serializable
data class NewOwnerResponse(val token: String)

/** A saved location, as the API returns it. Coordinates come back rounded to 3 decimal places (~100 m). */
@Serializable
data class SavedLocation(
    val id: String,
    val name: String,
    val lat: Double,
    val lon: Double,
    val radiusKm: Double,
)

/** What `POST /api/locations` takes. The server trims the name and rounds the coordinates. */
@Serializable
data class SavedLocationRequest(
    val name: String,
    val lat: Double,
    val lon: Double,
    val radiusKm: Double,
)

/** `GET /api/flights`: aircraft within `radiusKm` of the centre, nearest first. */
@Serializable
data class FlightsResponse(
    val radiusKm: Double,
    val count: Int,
    val fetchedAt: String,
    val flights: List<Flight>,
)

/** One aircraft. Only the fields the app uses; the rest of the API's FlightDto is ignored. */
@Serializable
data class Flight(
    val hex: String,
    val callsign: String? = null,
    val airline: String? = null,
    val route: Route? = null,
    val airportGuess: AirportGuess? = null,
    val registration: String? = null,
    val aircraftType: String? = null,
    val distanceKm: Double,
    val altitudeFt: Int? = null,
    val onGround: Boolean,
)

/** The airports in order: origin first, destination last. */
@Serializable
data class Route(val airports: List<Airport>)

@Serializable
data class Airport(val icao: String, val iata: String? = null, val name: String, val city: String? = null)

/** Only when there's no route: `kind` is "From", "To" or "At". */
@Serializable
data class AirportGuess(val kind: String, val airport: Airport)
