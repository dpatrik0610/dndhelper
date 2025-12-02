using System;
using System.Collections.Generic;

namespace dndhelper.Utils
{
    public static class Guard
    {
        #region Null / Empty

        public static T NotNull<T>(T? value, string paramName) where T : class
        {
            if (value is null)
                throw new ArgumentNullException(paramName);

            return value;
        }

        public static string NotNullOrWhiteSpace(string? value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentNullException(paramName);

            return value;
        }

        public static IReadOnlyCollection<T> NotNullOrEmpty<T>(ICollection<T>? value, string paramName)
        {
            if (value == null || value.Count == 0)
                throw new ArgumentException("Value cannot be null or empty.", paramName);

            return (IReadOnlyCollection<T>)value;
        }

        #endregion


        #region Numeric - Non-negative / Greater than zero

        public static int NotNegative(int value, string paramName)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(paramName, "Value cannot be negative.");

            return value;
        }

        public static decimal NotNegative(decimal value, string paramName)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(paramName, "Value cannot be negative.");

            return value;
        }

        public static int GreaterThanZero(int value, string paramName)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(paramName, "Value must be greater than zero.");

            return value;
        }

        public static decimal GreaterThanZero(decimal value, string paramName)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(paramName, "Value must be greater than zero.");

            return value;
        }

        #endregion


        #region Numeric - Range checks

        public static int InRange(int value, int min, int max, string paramName)
        {
            if (value < min || value > max)
                throw new ArgumentOutOfRangeException(paramName, $"Value must be between {min} and {max}.");

            return value;
        }

        public static decimal InRange(decimal value, decimal min, decimal max, string paramName)
        {
            if (value < min || value > max)
                throw new ArgumentOutOfRangeException(paramName, $"Value must be between {min} and {max}.");

            return value;
        }

        #endregion


        #region Enum validation

        public static TEnum ValidEnum<TEnum>(TEnum value, string paramName)
            where TEnum : struct, Enum
        {
            if (!Enum.IsDefined(typeof(TEnum), value))
                throw new ArgumentException($"Invalid value for enum {typeof(TEnum).Name}.", paramName);

            return value;
        }

        #endregion


        #region Custom predicate

        public static void That(bool condition, string message, string paramName = "value")
        {
            if (!condition)
                throw new ArgumentException(message, paramName);
        }

        #endregion
    }
}
