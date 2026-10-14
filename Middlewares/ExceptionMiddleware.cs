using dndhelper.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            context.Response.ContentType = "application/json";

            (HttpStatusCode status, string message) = ex switch
            {
                NotFoundException or KeyNotFoundException => (HttpStatusCode.NotFound, ex.Message),
                ConcurrencyException => (HttpStatusCode.Conflict, ex.Message),
                ArgumentException or FormatException => (HttpStatusCode.BadRequest, ex.Message),
                InvalidOperationException => (HttpStatusCode.BadRequest, ex.Message),
                ForbiddenException => (HttpStatusCode.Forbidden, ex.Message),
                UnauthorizedAccessException => (HttpStatusCode.Unauthorized, ex.Message),
                AlreadyExistsException => (HttpStatusCode.Conflict, ex.Message),
                RateLimitException => (HttpStatusCode.TooManyRequests, ex.Message),
                _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred.")
            };

            if (status == HttpStatusCode.InternalServerError)
                _logger.LogError(ex, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
            else
                _logger.LogWarning("{Status} on {Method} {Path}: {Message}", (int)status, context.Request.Method, context.Request.Path, ex.Message);

            context.Response.StatusCode = (int)status;
            if (ex is RateLimitException { RetryAfter: { } retryAfter })
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();

            var response = JsonConvert.SerializeObject(new { Message = message });
            await context.Response.WriteAsync(response);
        }
    }
}
