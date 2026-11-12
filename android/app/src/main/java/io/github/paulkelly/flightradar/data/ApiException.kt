package io.github.paulkelly.flightradar.data

import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull
import retrofit2.HttpException

/** The API answered with an error status. `message` is the most useful sentence from its ProblemDetails body. */
class ApiException(val status: Int, message: String) : Exception(message)

/** Retrofit's HttpException, with the ProblemDetails body read into a message. */
fun HttpException.toApiException(): ApiException {
    val body = try {
        response()?.errorBody()?.string()
    } catch (_: java.io.IOException) {
        null
    }
    return ApiException(code(), problemMessage(body) ?: "The server answered ${code()}.")
}

/**
 * The most useful sentence in a ProblemDetails body, like the web app's `problemMessage`: the first validation error
 * ("name must be at most 50 characters."), else `detail` ("You already have a location called Home."), else
 * `title`. Null if the body isn't a ProblemDetails object.
 */
fun problemMessage(body: String?): String? {
    if (body.isNullOrBlank()) return null
    val problem = try {
        FlightRadarApi.json.parseToJsonElement(body) as? JsonObject
    } catch (_: IllegalArgumentException) { // SerializationException is one: not JSON
        null
    } ?: return null

    val firstError = (problem["errors"] as? JsonObject)
        ?.values
        ?.firstNotNullOfOrNull { messages -> ((messages as? JsonArray)?.firstOrNull() as? JsonPrimitive)?.contentOrNull }
    return firstError
        ?: (problem["detail"] as? JsonPrimitive)?.contentOrNull
        ?: (problem["title"] as? JsonPrimitive)?.contentOrNull
}
