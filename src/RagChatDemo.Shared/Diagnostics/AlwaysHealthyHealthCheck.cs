using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RagChatDemo.Shared.Diagnostics;

/// <summary>Always returns Healthy - used as the "self" check tagged "live".</summary>
public sealed class AlwaysHealthyHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(HealthCheckResult.Healthy());
}
