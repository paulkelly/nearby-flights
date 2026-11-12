package io.github.paulkelly.flightradar.notify

import io.github.paulkelly.flightradar.widget.WidgetAircraft
import io.github.paulkelly.flightradar.widget.WidgetSnapshot
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class NearbyNotificationContentTest {
    private val easyJet = WidgetAircraft(
        title = "easyJet",
        name = "EZY151G",
        airlineCode = "EZY",
        route = "LGW → BHD",
        aircraftType = "A319",
        distanceKm = 3.1,
        altitudeFt = 1275,
        onGround = false,
        verticalRateFpm = -800,
    )

    private fun snapshot(aircraft: WidgetAircraft?, count: Int = 1) = WidgetSnapshot(aircraft, count, updatedAtMs = 0)

    @Test
    fun anAirlineFlight_nameAndRouteOnly() {
        val content = nearbyNotificationContent("Home", snapshot(easyJet, count = 5))

        // No distance, altitude, type or count: the widget has those.
        assertEquals(NearbyNotificationContent(header = "Home", title = "easyJet EZY151G", text = "LGW → BHD"), content)
    }

    @Test
    fun noAirline_titleIsTheCallsignOrRegistration() {
        val privatePlane = WidgetAircraft(title = "EI-ABC", distanceKm = 1.0, onGround = false)

        assertEquals("EI-ABC", nearbyNotificationContent("Home", snapshot(privatePlane))?.title)
    }

    @Test
    fun routeUnknown_noText() =
        assertNull(nearbyNotificationContent("Home", snapshot(easyJet.copy(route = null)))?.text)

    @Test
    fun onlyAGuess_showsIt() =
        assertEquals("From DUB", nearbyNotificationContent("Home", snapshot(easyJet.copy(route = "From DUB")))?.text)

    @Test
    fun nothingNear_noNotification() = assertNull(nearbyNotificationContent("Home", snapshot(null, count = 0)))

    @Test
    fun onlyOnTheGround_noNotification() =
        assertNull(nearbyNotificationContent("Home", snapshot(easyJet.copy(onGround = true, altitudeFt = null))))
}
