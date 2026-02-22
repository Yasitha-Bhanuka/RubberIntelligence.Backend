using MongoDB.Driver;
using RubberIntelligence.API.Domain.Entities;
using RubberIntelligence.API.Domain.Enums;

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

        public async Task<List<User>> GetNearbyFarmersAsync(double longitude, double latitude, double maxDistanceMeters)
        {
            var filter = Builders<User>.Filter.And(
                Builders<User>.Filter.Eq(u => u.Role, UserRole.Farmer),
                Builders<User>.Filter.NearSphere(u => u.Location, longitude, latitude, maxDistanceMeters)
            );

            return await _context.Users.Find(filter).ToListAsync();
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            return await _context.Users
                .Find(_ => true)
                .SortByDescending(u => u.CreatedAt)
                .ToListAsync();
        }

        public async Task DeleteAsync(Guid id)
        {
            await _context.Users.DeleteOneAsync(u => u.Id == id);
        }
    }
}

