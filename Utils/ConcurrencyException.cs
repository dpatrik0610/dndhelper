using System;

namespace dndhelper.Utils
{
    public class ConcurrencyException : Exception
    {
        public ConcurrencyException()
            : base("The room was modified by another action. Please retry.") { }

        public ConcurrencyException(string message)
            : base(message) { }
    }
}
