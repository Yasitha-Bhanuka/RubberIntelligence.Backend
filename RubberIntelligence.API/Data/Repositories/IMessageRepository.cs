using RubberIntelligence.API.Modules.Dpp.Models;

namespace RubberIntelligence.API.Data.Repositories
{
    public interface IMessageRepository
    {
        Task CreateAsync(Message message);
        Task<List<Message>> GetByLotIdAsync(string lotId);
    }
}
