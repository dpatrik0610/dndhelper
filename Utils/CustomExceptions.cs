using Serilog;
using System;

namespace dndhelper.Utils
{
    public static class CustomExceptions
    {
        public static Exception ThrowArgumentException(ILogger logger, string paramName)
        {
            logger.Error($"Parameter '{paramName}' is invalid or empty.");
            throw new ArgumentException($"Parameter '{paramName}' is invalid or empty.", paramName);
        }

        public static Exception ThrowArgumentNullException(ILogger logger, string paramName)
        {
            logger.Error($"Parameter '{paramName}' cannot be null.");
            throw new ArgumentNullException(paramName, $"Parameter '{paramName}' cannot be null.");
        }

        public static Exception ThrowInvalidOperationException(ILogger logger, string paramName)
        {
            logger.Error($"Operation is invalid for parameter '{paramName}'.");
            throw new InvalidOperationException($"Operation is invalid for parameter '{paramName}'.");
        }

        public static Exception ThrowUnauthorizedAccessException(ILogger logger, string paramName)
        {
            logger.Error($"Unauthorized access for parameter '{paramName}'.");
            throw new UnauthorizedAccessException($"Unauthorized access for parameter '{paramName}'.");
        }

        public static Exception ThrowApplicationException(ILogger logger, string paramName)
        {
            logger.Error($"Application error related to parameter '{paramName}'.");
            throw new ApplicationException($"Application error related to parameter '{paramName}'.");
        }

        public static Exception ThrowNotFoundException(ILogger logger, string paramName)
        {
            logger.Error($"Resource not found for parameter '{paramName}'.");
            throw new Exception($"Resource not found for parameter '{paramName}'.");
        }
    }
}
