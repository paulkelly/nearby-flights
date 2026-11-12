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
