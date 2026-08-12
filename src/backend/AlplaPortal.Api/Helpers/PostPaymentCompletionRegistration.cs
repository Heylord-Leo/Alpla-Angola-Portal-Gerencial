using AlplaPortal.Domain.Configuration;

namespace AlplaPortal.Api.Helpers;

/// <summary>
/// Registration of <see cref="PostPaymentCompletionOptions"/>.
///
/// <para>This lives in its own method — rather than inline in <c>Program.cs</c> — so the exact
/// production binding path can be covered by a test. The original inline
/// <c>Configure&lt;T&gt;(GetSection(...))</c> registration compiled, started cleanly and passed
/// every unit test, yet returned 500 on every RequestsController endpoint at runtime: the default
/// <c>ConfigurationBinder</c> overflowed on the "never" sentinel date, and options bind lazily, so
/// the failure only appeared when a controller that injects them was first constructed.</para>
/// </summary>
public static class PostPaymentCompletionRegistration
{
    /// <summary>
    /// Binds the "PostPaymentCompletion" section without using <c>ConfigurationBinder</c>'s
    /// date conversion. Fails closed: anything missing or unparseable yields a disabled feature
    /// with an unreachable effective date.
    /// </summary>
    public static IServiceCollection AddPostPaymentCompletionOptions(
        this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PostPaymentCompletionOptions.SectionName);

        services.Configure<PostPaymentCompletionOptions>(options =>
        {
            var bound = PostPaymentCompletionOptions.FromConfigurationValues(
                section["Enabled"],
                section["EffectiveDateUtc"],
                section["CompletionEnabled"]);

            options.Enabled = bound.Enabled;
            options.CompletionEnabled = bound.CompletionEnabled;
            options.EffectiveDateUtc = bound.EffectiveDateUtc;
        });

        return services;
    }
}
