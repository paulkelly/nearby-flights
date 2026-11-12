package io.github.paulkelly.flightradar.widget

import io.github.paulkelly.flightradar.data.Airport
import io.github.paulkelly.flightradar.data.AirportGuess
import io.github.paulkelly.flightradar.data.Flight
import io.github.paulkelly.flightradar.data.Route
import java.text.NumberFormat
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

class WidgetModelTest {
    private fun flight(
        hex: String,
        distanceKm: Double,
        onGround: Boolean = false,
        callsign: String? = null,
        airline: String? = null,
        registration: String? = null,
    ) = Flight(
        hex = hex,
        callsign = callsign,
        airline = airline,
        registration = registration,
        distanceKm = distanceKm,
        onGround = onGround,
    )

    private val dublin = Airport(icao = "EIDW", iata = "DUB", name = "Dublin Airport", city = "Dublin")
    private val stansted = Airport(icao = "EGSS", iata = "STN", name = "London Stansted Airport", city = "London")

    @Test
    fun chooseForWidget_prefersTheNearestInTheAir() {
        val parked = flight("a", 0.5, onGround = true)
        val flying = flight("b", 4.0)
        assertEquals(flying, chooseForWidget(listOf(parked, flying)))
    }

    @Test
    fun chooseForWidget_nothingFlying_showsTheNearestOnTheGround() {
        val near = flight("a", 0.5, onGround = true)
        val far = flight("b", 2.0, onGround = true)
        assertEquals(near, chooseForWidget(listOf(near, far)))
    }

    @Test
    fun chooseForWidget_none_isNull() {
        assertNull(chooseForWidget(emptyList()))
    }

    @Test
    fun toWidgetAircraft_airline_isTheTitleWithTheCallsignBeside() {
        val aircraft = toWidgetAircraft(flight("a", 1.0, callsign = "RYR2DQ", airline = "Ryanair"))
        assertEquals("Ryanair", aircraft.title)
        assertEquals("RYR2DQ", aircraft.callsign)
    }

    @Test
    fun toWidgetAircraft_noAirline_fallsBackToCallsignThenRegistrationThenHex() {
        assertEquals("EIGWY", toWidgetAircraft(flight("a", 1.0, callsign = "EIGWY")).title)
        assertNull(toWidgetAircraft(flight("a", 1.0, callsign = "EIGWY")).callsign) // it's already the title
        assertEquals("EI-GWY", toWidgetAircraft(flight("a", 1.0, callsign = " ", registration = "EI-GWY")).title)
        assertEquals("4CA2D6", toWidgetAircraft(flight("4ca2d6", 1.0)).title)
    }

    @Test
    fun routeShort_route_isTheCodesInOrder() {
        val withRoute = flight("a", 1.0).copy(route = Route(listOf(dublin, stansted)))
        assertEquals("DUB → STN", routeShort(withRoute))
    }

    @Test
    fun routeShort_noIata_usesIcao() {
        val noIata = dublin.copy(iata = null)
        assertEquals("EIDW → STN", routeShort(flight("a", 1.0).copy(route = Route(listOf(noIata, stansted)))))
    }

    @Test
    fun routeShort_guess_isKindAndCode() {
        assertEquals("From DUB", routeShort(flight("a", 1.0).copy(airportGuess = AirportGuess("From", dublin))))
    }

    @Test
    fun routeShort_nothingKnown_isNull() {
        assertNull(routeShort(flight("a", 1.0)))
    }

    @Test
    fun formatDistance_oneDecimalBelowTenKm() {
        assertEquals("3.4 km", formatDistance(3.44))
        assertEquals("27 km", formatDistance(26.7))
    }

    @Test
    fun formatAltitude_groundUnknownAndFeet() {
        val aircraft = toWidgetAircraft(flight("a", 1.0))
        assertEquals("On the ground", formatAltitude(aircraft.copy(onGround = true)))
        assertNull(formatAltitude(aircraft.copy(altitudeFt = null)))
        // The thousands separator follows the phone's language ("36,000" in English, "36.000" in German).
        assertEquals("${NumberFormat.getIntegerInstance().format(36000)} ft", formatAltitude(aircraft.copy(altitudeFt = 36000)))
    }
}
