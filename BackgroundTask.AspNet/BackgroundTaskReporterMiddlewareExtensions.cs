using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace BackgroundTask.AspNet
{
    public static class BackgroundTaskReporterMiddlewareExtensions
    {
        public static IEndpointConventionBuilder MapBackgroundReporterEndpoint(
            this IEndpointRouteBuilder endpoints, string pattern)
        {
            var pipeline = endpoints.CreateApplicationBuilder()
                .UseMiddleware<BackgroundTaskReporterMiddleware>()
                .Build();

            return endpoints.MapGet(pattern, pipeline).WithDisplayName("Background Task Reporter");
        }
    }
}
