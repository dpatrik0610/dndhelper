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

        public static NotFoundException ThrowNotFoundException(ILogger logger, string paramName)
        {
            logger.Error($"Resource not found for parameter '{paramName}'.");
            throw new NotFoundException($"Resource not found for parameter '{paramName}'.");
        }

        public static Exception ThrowCustomException(ILogger logger, string message)
        {
            logger.Error(message);
            return new Exception(message);
        }
    }

    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message) { }
    }

    public class ApplicationException : Exception
    {
        public ApplicationException(string message) : base(message) { }
    }

    public class AlreadyExistsException : Exception
    {
        public AlreadyExistsException(string message) : base(message) { }
    }
}