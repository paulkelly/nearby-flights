package io.github.paulkelly.flightradar.widget

import io.github.paulkelly.flightradar.data.Airport
import io.github.paulkelly.flightradar.data.AirportGuess
import io.github.paulkelly.flightradar.data.Flight
import io.github.paulkelly.flightradar.data.Route
import java.util.Locale
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Before
import org.junit.Test

class WidgetModelTest {
    // Number formats follow the phone's language; pin it so "36,000 ft" is the same on every machine.
    private lateinit var savedLocale: Locale

    @Before
    fun pinLocale() {
        savedLocale = Locale.getDefault()
        Locale.setDefault(Locale.UK)
    }

    @After
    fun restoreLocale() {
        Locale.setDefault(savedLocale)
    }

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
    fun toWidgetAircraft_airline_isTheTitleWithTheFlightsNameBelow() {
        val aircraft = toWidgetAircraft(flight("a", 1.0, callsign = "RYR2DQ", airline = "Ryanair"))
        assertEquals("Ryanair", aircraft.title)
        assertEquals("RYR2DQ", aircraft.name)
    }

    @Test
    fun toWidgetAircraft_airlineWithoutCallsign_nameFallsBackToRegistration() {
        val aircraft = toWidgetAircraft(flight("a", 1.0, airline = "Ryanair", registration = "EI-DAC"))
        assertEquals("EI-DAC", aircraft.name)
    }

    @Test
    fun toWidgetAircraft_noAirline_fallsBackToCallsignThenRegistrationThenHex() {
        assertEquals("EIGWY", toWidgetAircraft(flight("a", 1.0, callsign = "EIGWY")).title)
        assertNull(toWidgetAircraft(flight("a", 1.0, callsign = "EIGWY")).name) // it's already the title
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
        assertNull(routeLong(flight("a", 1.0)))
    }

    @Test
    fun routeLong_route_isCityAndCode() {
        val withRoute = flight("a", 1.0).copy(route = Route(listOf(dublin, stansted)))
        assertEquals("Dublin (DUB) → London (STN)", routeLong(withRoute))
    }

    @Test
    fun routeLong_noCity_usesTheAirportsName() {
        val noCity = dublin.copy(city = null)
        assertEquals("From Dublin Airport (DUB)", routeLong(flight("a", 1.0).copy(airportGuess = AirportGuess("From", noCity))))
    }

    @Test
    fun formatDistance_oneDecimal() {
        assertEquals("3.4 km", formatDistance(3.44))
        assertEquals("27.0 km", formatDistance(26.96))
    }

    @Test
    fun formatRadius_wholeKmWithoutDecimals() {
        assertEquals("10 km", formatRadius(10.0))
        assertEquals("2.5 km", formatRadius(2.5))
    }

    @Test
    fun formatAltitude_groundUnknownAndFeet() {
        val aircraft = toWidgetAircraft(flight("a", 1.0))
        assertEquals("On ground", formatAltitude(aircraft.copy(onGround = true)))
        assertEquals("Unknown", formatAltitude(aircraft.copy(altitudeFt = null)))
        assertEquals("36,000 ft", formatAltitude(aircraft.copy(altitudeFt = 36000)))
    }

    @Test
    fun formatSpeedAndTrack_roundedOrUnknown() {
        assertEquals("412 kt", formatSpeed(411.6))
        assertEquals("Unknown", formatSpeed(null))
        assertEquals("275°", formatTrack(274.8))
        assertEquals("Unknown", formatTrack(null))
    }

    @Test
    fun formatVerticalRate_directionLevelOrUnknown() {
        assertEquals("↑ 1,500 ft/min", formatVerticalRate(1500))
        assertEquals("↓ 800 ft/min", formatVerticalRate(-800))
        assertEquals("Level", formatVerticalRate(64))
        assertEquals("Level", formatVerticalRate(-99))
        assertEquals("Unknown", formatVerticalRate(null))
    }

    @Test
    fun badgeHue_isTheWebsHash() {
        // Worked out by hand from web/src/components/airlineBadge.ts: the same airline, the same colour.
        assertEquals(283, badgeHue("RYR"))
        assertEquals(250, badgeHue("EIN"))
    }
}
