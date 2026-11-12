package io.github.paulkelly.flightradar.push

import io.github.paulkelly.flightradar.data.FlightsResponse
import io.github.paulkelly.flightradar.widget.WidgetSnapshot
import io.github.paulkelly.flightradar.widget.snapshotOf
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class NearestPushTest {
    // 2026-11-12T19:45:52.399Z, written as the API writes it (.NET "O": 7 decimals and an offset).
    private val fetchedAt = "2026-11-12T19:45:52.3990494+00:00"
    private val fetchedAtMs = 1_794_512_752_399L

    // As the API's FlightDto serialises, including fields the app doesn't use (they must be ignored).
    private val flightJson = """{"hex":"4ca123","callsign":"EIN123","airline":"Aer Lingus","airlineCode":"EIN",
        "route":{"airports":[{"icao":"EIDW","iata":"DUB","name":"Dublin Airport","city":"Dublin"},
        {"icao":"EGLL","iata":"LHR","name":"Heathrow","city":"London"}]},"airportGuess":null,"registration":"EI-DEA",
        "aircraftType":"A320","lat":53.43,"lon":-6.27,"positionTimestamp":"2026-11-12T19:45:50+00:00",
        "distanceKm":4.2,"altitudeFt":12000,"onGround":false,"groundSpeedKt":250.5,"trackDeg":90,
        "verticalRateFpm":1500}"""

    private fun data(vararg pairs: Pair<String, String>) =
        mapOf("type" to "nearest", "locationId" to "home-id", "fetchedAt" to fetchedAt) + pairs

    @Test
    fun parse_withAnAircraft_readsItAll() {
        val push = NearestPush.parse(data("flight" to flightJson, "count" to "3"))!!

        assertEquals("home-id", push.locationId)
        assertEquals(fetchedAtMs, push.fetchedAtMs)
        assertEquals("4ca123", push.flight?.hex)
        assertEquals("Aer Lingus", push.flight?.airline)
        assertEquals(3, push.count)
        assertFalse(push.flightOmitted)

        val snapshot = push.toSnapshot()
        assertEquals("Aer Lingus", snapshot.aircraft?.title)
        assertEquals("DUB → LHR", snapshot.aircraft?.route)
        assertEquals(fetchedAtMs, snapshot.updatedAtMs)
    }

    @Test
    fun parse_clearSkies_hasNoAircraft() {
        val push = NearestPush.parse(data("count" to "0"))!!

        assertNull(push.flight)
        assertNull(push.toSnapshot().aircraft)
        assertEquals(0, push.count)
    }

    @Test
    fun parse_noCount_guessesFromTheAircraft() {
        assertEquals(1, NearestPush.parse(data("flight" to flightJson))!!.count)
        assertEquals(0, NearestPush.parse(data())!!.count)
    }

    @Test
    fun parse_flightOmitted_saysSo() {
        assertTrue(NearestPush.parse(data("flightOmitted" to "true"))!!.flightOmitted)
    }

    @Test
    fun parse_notANearestPushOrUnreadable_isNull() {
        assertNull(NearestPush.parse(data() + ("type" to "something-else")))
        assertNull(NearestPush.parse(data() - "locationId"))
        assertNull(NearestPush.parse(data() + ("fetchedAt" to "yesterday")))
        assertNull(NearestPush.parse(data("flight" to "{not json")))
        assertNull(NearestPush.parse(data("flight" to """{"hex":"abc"}"""))) // missing required fields
    }

    @Test
    fun isNewer_onlyNewerDataReplacesWhatIsShown() {
        val push = NearestPush.parse(data())!!

        assertTrue(NearestPush.isNewer(push, null))
        assertTrue(NearestPush.isNewer(push, WidgetSnapshot(null, 0, fetchedAtMs - 1)))
        assertFalse(NearestPush.isNewer(push, WidgetSnapshot(null, 0, fetchedAtMs))) // already shown
        assertFalse(NearestPush.isNewer(push, WidgetSnapshot(null, 0, fetchedAtMs + 1))) // a later refresh won
    }

    @Test
    fun snapshotOf_usesTheServersFetchTime_soPushesCompareOnTheSameClock() {
        val snapshot = snapshotOf(FlightsResponse(10.0, 0, fetchedAt, emptyList()), nowMs = 42)

        assertEquals(fetchedAtMs, snapshot.updatedAtMs)
        assertEquals(42, snapshotOf(FlightsResponse(10.0, 0, "garbled", emptyList()), nowMs = 42).updatedAtMs)
    }
}
