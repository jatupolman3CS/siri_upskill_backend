using Microsoft.AspNetCore.Http;
using Siri.Api.Middleware;

namespace Siri.UnitTests.Security;

public sealed class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_AddsOwaspSecurityHeaders()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.IsHttps = true;

        var middleware = new SecurityHeadersMiddleware(innerContext =>
        {
            innerContext.Response.StatusCode = 200;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var headers = context.Response.Headers;
        Assert.Equal("nosniff", headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", headers["X-Frame-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", headers["Referrer-Policy"].ToString());
        Assert.Contains("default-src 'self'", headers["Content-Security-Policy"].ToString());
        Assert.Contains("max-age=31536000", headers["Strict-Transport-Security"].ToString());
    }
}
