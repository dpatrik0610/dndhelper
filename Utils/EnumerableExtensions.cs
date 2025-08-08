using System.Collections.Generic;
using System.Linq;

namespace dndhelper.Utils
{
    public static class EnumerableExtensions
    {
        public static bool IsNullOrEmpty<T>(this IEnumerable<T>? source)
        {
            return source == null || !source.Any();
        }
    }
}
