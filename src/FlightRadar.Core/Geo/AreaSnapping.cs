namespace FlightRadar.Core.Geo;

/// <summary>A search circle: centre + radius.</summary>
public readonly record struct SearchArea(GeoPoint Centre, double RadiusKm);

/// <summary>
/// Turns an exact search circle into a "snapped" one that nearby requests share, so they can share a cache entry.
/// </summary>
/// <remarks>
/// <para>
/// The rules (see AreaSnappingTests):
/// <list type="number">
/// <item>Coverage: the snapped circle contains the whole original circle, so no aircraft is ever missed.
///   (NearbyFlightsService trims the result back to the exact original circle afterwards.)</item>
/// <item>Sharing: requests a few hundred metres apart with the same radius get the same snapped circle.</item>
/// <item>Cost: the snapped circle isn't much bigger than the original (bigger = more data from upstream).</item>
/// <item>The snapped radius never exceeds <c>maxRadiusKm</c> (the provider's limit).</item>
/// </list>
/// </para>
/// <para>
/// How: only the <em>centre</em> is snapped, not the area. The circle is never split into cells; each request
/// still becomes one circle, one cache key and (on a miss) one provider call. The cell only decides which
/// snapped circle a request gets.
/// </para>
/// <para>
/// The grid is in 3D, not latitude/longitude. The centre is converted to Earth-centred X/Y/Z coordinates (km),
/// each coordinate is rounded to the nearest multiple of <see cref="CellSizeKm"/>, and the result is projected
/// back onto the surface. A lat/lon grid's cells shrink towards the poles (a degree of longitude is ~111 km at
/// the equator, ~67 km at Dublin, 0 at the poles) and wrap at ±180°; 3D cubes are the same size everywhere,
/// so neither needs special handling. The sphere cuts through the cubes at all angles, so the surface patches
/// that share a key are irregular (some are small slivers); that costs a little sharing, never correctness.
/// </para>
/// <para>
/// The radius is enlarged by the furthest any centre can move (<see cref="MaxCentreShiftKm"/>), not by how far
/// <em>this</em> centre moved, then rounded up to a <see cref="CellSizeKm"/> bucket. So the snapped radius
/// depends only on the requested radius, and every centre in a cell gets an identical key. Bucketing also lets
/// slightly different radii (50.1, 50.4, 50.9 km) share. The extra radius is between 8.66 and 18.66 km.
/// </para>
/// <para>
/// If the enlarged radius would pass the provider's limit, the original circle is returned unchanged: it still
/// covers itself and is still cached under its own exact key, it just isn't shared. With the public limit at
/// 250 km and adsb.lol's at 463 km, real requests don't reach this; it's a safety net.
/// </para>
/// </remarks>
public static class AreaSnapping
{
    private readonly record struct CartesianPoint(double X, double Y, double Z);

    /// <summary>Grid spacing in 3D, and the radius bucket size. Bigger = more sharing, bigger downloads.</summary>
    private const double CellSizeKm = 10;

    /// <summary>
    /// The furthest, along the surface, that snapping can move a centre (≈ 8.66 km).
    /// </summary>
    /// <remarks>
    /// Rounding each of X, Y and Z by at most half a cell moves a point at most half a cube's diagonal in 3D:
    /// d = √3/2 × <see cref="CellSizeKm"/>. Coverage is measured along the surface after projecting back, and a
    /// 3D move of d can be slightly longer as an arc: R × asin(d / R). For 10 km cells that's 2.7 mm more than d,
    /// irrelevant in practice, but the coverage rule is a guarantee, so the margin uses the exact bound.
    /// </remarks>
    private static readonly double MaxCentreShiftKm =
        Haversine.EarthRadiusKm * Math.Asin(CellSizeKm * Math.Sqrt(3) / 2 / Haversine.EarthRadiusKm);

    /// <summary>
    /// The circle to fetch and cache instead of (<paramref name="centre"/>, <paramref name="radiusKm"/>).
    /// </summary>
    /// <param name="maxRadiusKm">The provider's limit (adsb.lol: 250 nm ≈ 463 km), not the public API's.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radiusKm"/> is over the limit: no answer
    /// could both cover it and stay within the limit.</exception>
    public static SearchArea Snap(GeoPoint centre, double radiusKm, double maxRadiusKm)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(radiusKm, maxRadiusKm);

        GeoPoint snappedCentre = FindCentrePointOfClosestCell(centre);
        double radiusBucket = Math.Ceiling((radiusKm + MaxCentreShiftKm) / CellSizeKm) * CellSizeKm;

        if (radiusBucket > maxRadiusKm)
        {
            return new SearchArea(centre, radiusKm);
        }

        return new SearchArea(snappedCentre, radiusBucket);
    }

    // A "cell" is the cube of points that round to the same grid point, so the grid point is the cell's centre.
    private static GeoPoint FindCentrePointOfClosestCell(GeoPoint geoPoint)
    {
        CartesianPoint snappedCartesianPoint = FindCentrePointOfClosestCell(ConvertGeoToCartesian(geoPoint));
        return ConvertCartesianToGeo(snappedCartesianPoint);
    }

    private static CartesianPoint FindCentrePointOfClosestCell(CartesianPoint cartesianPoint) =>
        new CartesianPoint(
            RoundToGridSize(cartesianPoint.X),
            RoundToGridSize(cartesianPoint.Y),
            RoundToGridSize(cartesianPoint.Z));

    // Earth-centred coordinates on a sphere of radius EarthRadiusKm (the same sphere Haversine uses):
    // X towards lat 0/lon 0, Y towards lat 0/lon 90°E, Z towards the north pole.
    private static CartesianPoint ConvertGeoToCartesian(GeoPoint geoPoint)
    {
        double latRadians = double.DegreesToRadians(geoPoint.Latitude);
        double longRadians = double.DegreesToRadians(geoPoint.Longitude);

        return new CartesianPoint(
            Haversine.EarthRadiusKm * Math.Cos(latRadians) * Math.Cos(longRadians),
            Haversine.EarthRadiusKm * Math.Cos(latRadians) * Math.Sin(longRadians),
            Haversine.EarthRadiusKm * Math.Sin(latRadians));
    }

    // The inverse. Works for points off the surface too (a snapped point sits up to ~8.66 km inside or outside
    // it): only the direction from the Earth's centre matters, which is the same as projecting onto the surface.
    // Atan2 returns longitude in (-180°, 180°], so the antimeridian needs no special case.
    private static GeoPoint ConvertCartesianToGeo(CartesianPoint cartesianPoint)
    {
        double longitude = Math.Atan2(cartesianPoint.Y, cartesianPoint.X);
        double distanceFromAxis = Math.Sqrt(cartesianPoint.X * cartesianPoint.X + cartesianPoint.Y * cartesianPoint.Y);
        double latitude = Math.Atan2(cartesianPoint.Z, distanceFromAxis);
        return new GeoPoint(double.RadiansToDegrees(latitude), double.RadiansToDegrees(longitude));
    }

    // Floor(x + 0.5) rather than Math.Round: Math.Round's default rounds halves to even (2.5 → 2, 3.5 → 4), so
    // ties would go different ways in different places. Either way is correct; one consistent rule is easier.
    private static double RoundToGridSize(double pos) => Math.Floor(pos / CellSizeKm + 0.5) * CellSizeKm;
}
