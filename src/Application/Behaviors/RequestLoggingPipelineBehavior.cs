using Domain.Common;
using MediatR;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Application.Behaviors;

/// <summary>
/// Logs MediatR request start/completion and attaches Result errors to structured logs.
/// </summary>
public sealed class RequestLoggingPipelineBehavior<TRequest, TResponse>(
    ILogger<RequestLoggingPipelineBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : Result
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(next);

        var requestName = typeof(TRequest).Name;

        logger.LogInformation(RequestLoggingPipelineBehaviorDiagnostics.ProcessingRequest, requestName);

        var result = await next();

        if (result.IsSuccess)
        {
            logger.LogInformation(RequestLoggingPipelineBehaviorDiagnostics.CompletedRequest, requestName);
        }
        else
        {
            using (LogContext.PushProperty(RequestLoggingPipelineBehaviorDiagnostics.ErrorProperty, result.Error, true))
            {
                logger.LogError(RequestLoggingPipelineBehaviorDiagnostics.CompletedRequestWithError, requestName);
            }
        }

        return result;
    }
}
