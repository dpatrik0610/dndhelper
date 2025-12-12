using System.Collections.Generic;

namespace dndhelper.Services.Interfaces
{
    public interface ICacheService
    {
        void TrackKey(string key);
        void RemoveTracked(string key);
        List<string> GetAllKeys();
        void ClearAllTracked();
        void ClearAllFromCache();
        void ClearByPrefix(string prefix);
    }
}
