using MongoDB.Driver;
using RubberIntelligence.API.Domain.Entities;

namespace RubberIntelligence.API.Data.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly AppDbContext _context;

        public UserRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<User?> GetByIdAsync(Guid id)
        {
            return await _context.Users.Find(u => u.Id == id).FirstOrDefaultAsync();
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _context.Users.Find(u => u.Email == email).FirstOrDefaultAsync();
        }

        public async Task CreateUserAsync(User user)
        {
            await _context.Users.InsertOneAsync(user);
        }

        public async Task UpdateAsync(User user)
        {
            await _context.Users.ReplaceOneAsync(x => x.Id == user.Id, user);
        }

        public async Task<bool> ExistsAsync(string email)
        {
            return await _context.Users.Find(u => u.Email == email).AnyAsync();
        }
    }
}
