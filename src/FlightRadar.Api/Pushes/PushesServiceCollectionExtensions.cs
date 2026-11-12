using Microsoft.Extensions.Options;

namespace FlightRadar.Api.Pushes;

public static class PushesServiceCollectionExtensions
{
    /// <summary>
    /// Pushes to phones (step 9d): Firebase when <c>Firebase:ServiceAccountJson</c> is set, otherwise a sender that
    /// skips them. Optional like the OpenSky credentials, so local runs, tests and CI need no Firebase project.
    /// </summary>
    public static IServiceCollection AddPushes(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FirebaseOptions>()
            .BindConfiguration(FirebaseOptions.SectionName)
            // Checked at startup, so a broken key fails the deploy instead of every push. The message never
            // includes the key itself.
            .Validate(options => !options.Configured || IsServiceAccountKey(options.ServiceAccountJson!),
                "Firebase:ServiceAccountJson is set but isn't a service account key (the JSON file from Google Cloud).")
            .ValidateOnStart();

        var configured = !string.IsNullOrWhiteSpace(
            configuration[$"{FirebaseOptions.SectionName}:{nameof(FirebaseOptions.ServiceAccountJson)}"]);
        if (configured)
        {
            services.AddSingleton<IPushSender, FirebasePushSender>();
        }
        else
        {
            services.AddSingleton<IPushSender, NotConfiguredPushSender>();
        }
        return services;
    }

    private static bool IsServiceAccountKey(string json)
    {
        try
        {
            FirebasePushSender.LoadCredential(json);
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException
                                              or Newtonsoft.Json.JsonException or FormatException)
        {
            return false;
        }
    }
}
