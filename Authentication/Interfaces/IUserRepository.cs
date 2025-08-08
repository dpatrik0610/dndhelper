using System.Threading.Tasks;

namespace dndhelper.Authentication.Interfaces
{
    public interface IUserRepository
    {
        Task<User> GetByUsernameAsync(string username);
        Task CreateAsync(User user);
        Task<bool> CheckUserExists(string username);
    }
}
