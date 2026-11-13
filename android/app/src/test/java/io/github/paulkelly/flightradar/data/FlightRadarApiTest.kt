package io.github.paulkelly.flightradar.data

import kotlinx.coroutines.test.runTest
import mockwebserver3.MockResponse
import mockwebserver3.MockWebServer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Before
import org.junit.Test

/** What the app's requests look like on the wire (MockWebServer records them). */
class FlightRadarApiTest {
    private val server = MockWebServer()
    private lateinit var api: FlightRadarApi

    @Before
    fun setUp() {
        server.start()
        api = FlightRadarApi.create(server.url("/").toString())
    }

    @After
    fun tearDown() {
        server.close()
    }

    @Test
    fun flights_saysItIsABackgroundRefresh() = runTest {
        server.enqueue(
            MockResponse.Builder()
                .code(200)
                .body("""{"radiusKm":10,"count":0,"fetchedAt":"2026-11-13T12:00:00+00:00","flights":[]}""")
                .build(),
        )

        api.flights(53.35, -6.26, 10.0)

        val request = server.takeRequest()
        assertEquals("/api/flights", request.url.encodedPath)
        // Without it the server counts the widget as someone watching, and polls the area every 10 s for 30 s.
        assertEquals("true", request.headers["X-Background-Refresh"])
    }
}
