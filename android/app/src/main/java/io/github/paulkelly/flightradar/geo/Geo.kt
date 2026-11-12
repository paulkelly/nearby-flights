package io.github.paulkelly.flightradar.geo

import kotlin.math.PI
import kotlin.math.asin
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.ln
import kotlin.math.sin
import kotlin.math.sqrt

/** A point on the Earth in degrees. Longitude isn't wrapped: [circlePolygon] can return values past ±180 on purpose. */
data class LatLon(val lat: Double, val lon: Double)

/** The mean Earth radius, the same as the API (`Haversine.EarthRadiusKm`) and the web app (`EARTH_RADIUS_KM`). */
const val EARTH_RADIUS_KM = 6371.0

/** Great-circle distance in km (haversine), as the API measures "inside the radius". */
fun distanceKm(a: LatLon, b: LatLon): Double {
    val dLat = (b.lat - a.lat).toRadians()
    val dLon = (b.lon - a.lon).toRadians()
    val h = sin(dLat / 2).let { it * it } +
        cos(a.lat.toRadians()) * cos(b.lat.toRadians()) * sin(dLon / 2).let { it * it }
    return 2 * EARTH_RADIUS_KM * asin(sqrt(h.coerceAtMost(1.0)))
}

/**
 * Where you end up starting at [from] and travelling [distanceKm] along a great circle, setting off at [bearingDeg]
 * (0 = north, 90 = east). The "destination point" formula: on a sphere, the start, the pole and the end make a
 * triangle, and the spherical law of cosines gives the end's latitude; the longitude change comes from the same
 * triangle via atan2 (which keeps the quadrant right, like Mathf.Atan2 does for 2D angles).
 *
 * The longitude is [from]'s plus the change, not wrapped into ±180: a circle across the 180° line then stays one
 * continuous shape instead of a strip around the whole world.
 */
fun destination(from: LatLon, bearingDeg: Double, distanceKm: Double): LatLon {
    val angular = distanceKm / EARTH_RADIUS_KM // the distance as an angle at the Earth's centre (radians)
    val bearing = bearingDeg.toRadians()
    val lat1 = from.lat.toRadians()

    val lat2 = asin(sin(lat1) * cos(angular) + cos(lat1) * sin(angular) * cos(bearing))
    val dLon = atan2(sin(bearing) * sin(angular) * cos(lat1), cos(angular) - sin(lat1) * sin(lat2))
    return LatLon(lat2.toDegrees(), from.lon + dLon.toDegrees())
}

/**
 * The search circle as a closed polygon: [points] corners, each exactly [radiusKm] from [centre] by great-circle
 * distance, starting due north and going clockwise, plus the first corner again at the end (GeoJSON rings are closed).
 *
 * A "circle" on the map isn't a circle on screen: the map's Mercator projection stretches it north-south away from
 * the equator, so it's built from points on the ground rather than drawn as a round shape in pixels. Same idea as the
 * web app, where Leaflet's `<Circle>` does this for us.
 */
fun circlePolygon(centre: LatLon, radiusKm: Double, points: Int = 64): List<LatLon> {
    require(points >= 3) { "A polygon needs at least 3 points" }
    require(radiusKm > 0) { "The radius must be positive" }
    val ring = (0 until points).map { i -> destination(centre, 360.0 * i / points, radiusKm) }
    return ring + ring.first()
}

/**
 * The map zoom at which a circle of [radiusKm] around latitude [lat] spans [fraction] of a view [viewSizeDp] across
 * (its smaller side). MapLibre's world is 512 dp wide at zoom 0 and doubles per zoom level; Mercator shrinks the
 * ground by cos(latitude), so the same km take more dp further north.
 */
fun zoomToShow(radiusKm: Double, lat: Double, viewSizeDp: Double, fraction: Double = 0.75): Double {
    val metresPerDpAtZoom0 = 2 * PI * EARTH_RADIUS_KM * 1000 * cos(lat.toRadians()) / 512
    val wantedMetresPerDp = 2 * radiusKm * 1000 / (viewSizeDp * fraction)
    return (ln(metresPerDpAtZoom0 / wantedMetresPerDp) / ln(2.0)).coerceIn(0.0, 18.0)
}

private fun Double.toRadians() = this * PI / 180
private fun Double.toDegrees() = this * 180 / PI
