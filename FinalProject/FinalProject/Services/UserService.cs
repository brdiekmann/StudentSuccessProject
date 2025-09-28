
using FinalProject.Models.Entities;

namespace FinalProject.Services
{
    public class UserService : IUserService
    {
        private readonly List<User> _users = new();

        public async Task<IEnumerable<User>> GetAllUsersAsync() => await Task.FromResult(_users);
        public async Task<User?> GetUserByIdAsync(int id) => await Task.FromResult(_users.FirstOrDefault(p => p.Id == id.ToString()));
        public async Task AddUserAsync(User user) { _users.Add(user); await Task.CompletedTask; }
        public async Task UpdateUserAsync(User user)
        {
            var existing = _users.FirstOrDefault(p => p.Id == user.Id);
            if (existing != null) { _users.Remove(existing); _users.Add(user); }
            await Task.CompletedTask;
        }
        public async Task DeleteUserAsync(int id)
        {
            var user = _users.FirstOrDefault(p => p.Id == id.ToString());
            if (user != null) _users.Remove(user);
            await Task.CompletedTask;
        }

       
    }
}
