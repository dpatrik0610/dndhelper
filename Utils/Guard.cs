using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace dndhelper.Utils
{
    public static class Guard
    {
        #region Null / Empty

        public static T NotNull<T>([NotNull] T? value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : class
        {
            if (value is null)
                throw new ArgumentNullException(paramName);

            return value;
        }

        public static string NotNullOrWhiteSpace([NotNull] string? value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            // ArgumentNullException for null, ArgumentException for empty/whitespace
            ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);

            return value;
        }

        public static IReadOnlyCollection<T> NotNullOrEmpty<T>([NotNull] IReadOnlyCollection<T>? value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
        {
            if (value is null || value.Count == 0)
                throw new ArgumentException("Value cannot be null or empty.", paramName);

            return value;
        }

        #endregion


        #region Numeric

        public static T NotNegative<T>(T value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : INumber<T>
        {
            if (value < T.Zero)
                throw new ArgumentOutOfRangeException(paramName, value, "Value cannot be negative.");

            return value;
        }

        public static T GreaterThanZero<T>(T value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : INumber<T>
        {
            if (value <= T.Zero)
                throw new ArgumentOutOfRangeException(paramName, value, "Value must be greater than zero.");

            return value;
        }

        public static T InRange<T>(T value, T min, T max,
            [CallerArgumentExpression(nameof(value))] string? paramName = null) where T : INumber<T>
        {
            if (value < min || value > max)
                throw new ArgumentOutOfRangeException(paramName, value, $"Value must be between {min} and {max}.");

            return value;
        }

        #endregion


        #region Enum validation

        public static TEnum ValidEnum<TEnum>(TEnum value,
            [CallerArgumentExpression(nameof(value))] string? paramName = null)
            where TEnum : struct, Enum
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentException($"Invalid value for enum {typeof(TEnum).Name}.", paramName);

            return value;
        }

        #endregion


        #region Custom predicate

        public static void That(bool condition, string message, string? paramName = null)
        {
            if (!condition)
                throw new ArgumentException(message, paramName);
        }

        #endregion
    }
}
