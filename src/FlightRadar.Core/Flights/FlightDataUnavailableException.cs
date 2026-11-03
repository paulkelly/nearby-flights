namespace FlightRadar.Core.Flights;

/// <summary>
/// Thrown by an <see cref="IFlightDataProvider"/> when its upstream source can't be reached or returns an error.
/// The API maps this to 502 Bad Gateway, whatever the provider.
/// </summary>
public sealed class FlightDataUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
