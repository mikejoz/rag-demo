using Hangfire.Dashboard;

namespace RagChatDemo.IngestionWorker;

internal sealed class AllowAllDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => true;
}
