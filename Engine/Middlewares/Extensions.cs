using Microsoft.AspNetCore.Builder;

namespace JacRed.Engine.Middlewares
{
    public static class Extensions
    {
        public static IApplicationBuilder UseModHeaders(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ModHeaders>();
        }

        public static IApplicationBuilder UseProxyAPI(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ProxyAPI>();
        }
    }
}
