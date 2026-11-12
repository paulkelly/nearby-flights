package io.github.paulkelly.flightradar.data

import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import retrofit2.http.Body
import retrofit2.http.DELETE
import retrofit2.http.GET
import retrofit2.http.Header
import retrofit2.http.POST
import retrofit2.http.Path
import retrofit2.http.Query

/**
 * The Flight Radar API, as Retrofit sees it: an interface whose annotations describe each request, and Retrofit
 * generates the implementation (like Refit in .NET). `suspend` functions run the request without blocking the
 * calling thread and return the parsed body; any non-2xx answer throws `retrofit2.HttpException`.
 *
 * The owner token goes in a parameter rather than an interceptor, so it's visible which calls need one and the
 * repository decides which token (it changes when the server rejects one).
 */
interface FlightRadarApi {
    /** Live aircraft around a point. No token: anyone can ask (it's rate limited per IP). */
    @GET("api/flights")
    suspend fun flights(
        @Query("lat") lat: Double,
        @Query("lon") lon: Double,
        @Query("radiusKm") radiusKm: Double,
    ): FlightsResponse

    @POST("api/owners")
    suspend fun createOwner(): NewOwnerResponse

    @GET("api/locations")
    suspend fun listLocations(@Header("Authorization") authorization: String): List<SavedLocation>

    @POST("api/locations")
    suspend fun createLocation(
        @Header("Authorization") authorization: String,
        @Body request: SavedLocationRequest,
    ): SavedLocation

    @DELETE("api/locations/{id}")
    suspend fun deleteLocation(@Header("Authorization") authorization: String, @Path("id") id: String)

    companion object {
        /** Ignores fields this version of the app doesn't know, so the API can add some without breaking it. */
        val json = Json { ignoreUnknownKeys = true }

        /** `baseUrl` is the site's origin and must end with `/` (Retrofit's rule); paths above are relative to it. */
        fun create(baseUrl: String, client: OkHttpClient = OkHttpClient()): FlightRadarApi =
            Retrofit.Builder()
                .baseUrl(baseUrl)
                .client(client)
                .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
                .build()
                .create(FlightRadarApi::class.java)
    }
}

/** The value of an `Authorization` header for an owner token. */
fun bearer(token: String) = "Bearer $token"
