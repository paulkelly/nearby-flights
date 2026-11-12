package io.github.paulkelly.flightradar.ui.locations

import kotlin.math.ln
import kotlin.math.pow

/**
 * The radius slider's scale, the same as the web app's (`web/src/components/radiusScale.ts`): logarithmic, so
 * 1 → 2 km takes as much travel as 100 → 200 km, like a camera zoom slider. Compose's Slider works in fractions
 * (0 to 1); these convert between fractions and tidy kilometres (1 km steps below 20 km, 5 below 100, then 10).
 */
object RadiusScale {
    const val MIN_KM = 1.0

    /** The API's maximum radius (`FlightsEndpoints.MaxRadiusKm`): larger values get a 400. */
    const val MAX_KM = 250.0

    const val DEFAULT_KM = 10.0

    private const val RANGE_RATIO = MAX_KM / MIN_KM

    private fun stepKmAt(km: Double): Double = when {
        km < 20 -> 1.0
        km < 100 -> 5.0
        else -> 10.0
    }

    /** The nearest tidy value, within range. (Math.round rounds halves up, like JavaScript's.) */
    fun round(km: Double): Double {
        val step = stepKmAt(km)
        return (Math.round(km / step) * step).coerceIn(MIN_KM, MAX_KM)
    }

    /** Slider fraction → radius: MIN · ratio^fraction, tidied. */
    fun kmAt(fraction: Float): Double = round(MIN_KM * RANGE_RATIO.pow(fraction.coerceIn(0f, 1f).toDouble()))

    /** Radius → slider fraction: the inverse (a logarithm). */
    fun fractionFor(km: Double): Float = (ln(km.coerceIn(MIN_KM, MAX_KM) / MIN_KM) / ln(RANGE_RATIO)).toFloat()
}
