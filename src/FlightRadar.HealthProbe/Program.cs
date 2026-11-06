// Docker HEALTHCHECK for the chiseled API image, which has no shell, curl or wget to do this job.
//
//   dotnet FlightRadar.HealthProbe.dll [url]      (default: http://localhost:8080/health/ready)
//
// Exit code 0 = healthy, 1 = unhealthy: that's the whole contract with Docker. Whatever it prints is kept in the
// container's health log (`docker inspect --format "{{json .State.Health}}" <container>`), so print the answer.
//
// Docker runs this every few seconds, so it stays deliberately small: no hosting, DI or logging, only HttpClient.

var url = args.Length > 0 ? args[0] : "http://localhost:8080/health/ready";

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
try
{
    using var response = await http.GetAsync(url);
    Console.WriteLine($"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    return response.IsSuccessStatusCode ? 0 : 1; // Healthy and Degraded are 200; Unhealthy is 503
}
catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
{
    Console.WriteLine($"No answer from {url}: {ex.Message}");
    return 1;
}