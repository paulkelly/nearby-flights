package io.github.paulkelly.flightradar.notify

import io.github.paulkelly.flightradar.widget.WidgetAircraft
import io.github.paulkelly.flightradar.widget.WidgetSnapshot
import java.util.Locale
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Before
import org.junit.Test

class NearbyNotificationContentTest {
    private val defaultLocale = Locale.getDefault()

    @Before
    fun englishNumbers() = Locale.setDefault(Locale.UK) // "12,000" and "4.2", whatever the build machine's language

    @After
    fun restoreLocale() = Locale.setDefault(defaultLocale)

    private val aerLingus = WidgetAircraft(
        title = "Aer Lingus",
        name = "EIN123",
        airlineCode = "EIN",
        route = "DUB → LHR",
        aircraftType = "A320",
        distanceKm = 4.23,
        altitudeFt = 12000,
        onGround = false,
        verticalRateFpm = 1500,
    )

    private fun snapshot(aircraft: WidgetAircraft?, count: Int = 1) = WidgetSnapshot(aircraft, count, updatedAtMs = 0)

    @Test
    fun anAirlineFlight_readsLikeTheWidget() {
        val content = nearbyNotificationContent("Home", snapshot(aerLingus, count = 3))

        assertEquals(
            NearbyNotificationContent(
                header = "Home · 3 aircraft",
                title = "Aer Lingus EIN123 · 4.2 km",
                text = "DUB → LHR · 12,000 ft ↑ · A320",
            ),
            content,
        )
    }

    @Test
    fun oneAircraft_headerIsJustTheLocation() =
        assertEquals("Home", nearbyNotificationContent("Home", snapshot(aerLingus))?.header)

    @Test
    fun descendingLevelOrUnknownRate_arrowOrNone() {
        fun text(rate: Int?) = nearbyNotificationContent("Home", snapshot(aerLingus.copy(verticalRateFpm = rate)))?.text

        assertEquals("DUB → LHR · 12,000 ft ↓ · A320", text(-800))
        assertEquals("DUB → LHR · 12,000 ft · A320", text(50)) // under 100 ft/min counts as level
        assertEquals("DUB → LHR · 12,000 ft · A320", text(null))
    }

    @Test
    fun noAirlineRouteAltitudeOrType_leavesThemOut() {
        val privatePlane = WidgetAircraft(title = "EI-ABC", distanceKm = 1.0, onGround = false)

        val content = nearbyNotificationContent("Home", snapshot(privatePlane))

        assertEquals("EI-ABC · 1.0 km", content?.title)
        assertEquals("", content?.text)
    }

    @Test
    fun nothingNear_noNotification() = assertNull(nearbyNotificationContent("Home", snapshot(null, count = 0)))

    @Test
    fun onlyOnTheGround_noNotification() =
        assertNull(nearbyNotificationContent("Home", snapshot(aerLingus.copy(onGround = true, altitudeFt = null))))
}
