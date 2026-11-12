package io.github.paulkelly.flightradar.data

import java.net.HttpURLConnection.HTTP_NOT_FOUND
import java.net.HttpURLConnection.HTTP_UNAUTHORIZED
import retrofit2.HttpException

/**
 * This app's saved locations, with the same rules as the web app's `useSavedLocations`:
 * - The app is its own anonymous owner, created by the **first save**, never by just opening the app: someone who
 *   never saves leaves nothing in the database.
 * - A token the API rejects (401: the database was reset) is forgotten. Listing then shows nothing; a save becomes a
 *   new owner and tries once more.
 *
 * Errors: `ApiException` for an error answer (with the API's own message), `IOException` when the server can't be
 * reached. Callers decide what to show.
 */
class LocationsRepository(
    private val api: FlightRadarApi,
    private val tokens: OwnerTokenStore,
) {
    /** Oldest first, as the API returns them. None, without a request, before the first save. */
    suspend fun list(): List<SavedLocation> {
        val token = tokens.read() ?: return emptyList()
        return try {
            call { api.listLocations(bearer(token)) }
        } catch (e: ApiException) {
            if (e.status != HTTP_UNAUTHORIZED) throw e
            tokens.clear()
            emptyList()
        }
    }

    suspend fun save(request: SavedLocationRequest): SavedLocation {
        val token = tokens.read() ?: becomeNewOwner()
        return try {
            call { api.createLocation(bearer(token), request) }
        } catch (e: ApiException) {
            if (e.status != HTTP_UNAUTHORIZED) throw e
            // The stored token no longer works: start again as a new owner rather than fail the save.
            call { api.createLocation(bearer(becomeNewOwner()), request) }
        }
    }

    suspend fun delete(id: String) {
        val token = tokens.read() ?: return
        try {
            call { api.deleteLocation(bearer(token), id) }
        } catch (e: ApiException) {
            when (e.status) {
                HTTP_NOT_FOUND -> Unit // already gone: what was asked for
                HTTP_UNAUTHORIZED -> tokens.clear() // its locations are unreachable anyway
                else -> throw e
            }
        }
    }

    /**
     * A new owner, remembered on this phone. (Two saves at once could both create one, and the second token would
     * win; the screen allows one save at a time, so that doesn't arise.)
     */
    private suspend fun becomeNewOwner(): String {
        val token = call { api.createOwner() }.token
        tokens.write(token)
        return token
    }

    /** Runs one request, turning Retrofit's HttpException into our ApiException with the server's message. */
    private suspend fun <T> call(request: suspend () -> T): T =
        try {
            request()
        } catch (e: HttpException) {
            throw e.toApiException()
        }
}
