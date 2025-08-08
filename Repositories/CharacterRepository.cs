using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using MongoDB.Driver;
using System.Collections.Generic;
using System.Threading.Tasks;

public class CharacterRepository : ICharacterRepository
{
    private readonly IMongoCollection<Character> _characters;

    public CharacterRepository(MongoDbContext context)
    {
        _characters = context.GetCollection<Character>("Characters");
    }

    public async Task<IEnumerable<Character>> GetAllAsync()
    {
        return await _characters.Find(_ => true).ToListAsync();
    }

    public async Task<Character?> GetByIdAsync(string id)
    {
        return await _characters.Find(c => c.Id == id).FirstOrDefaultAsync();
    }

    public async Task<Character> AddAsync(Character character)
    {
        await _characters.InsertOneAsync(character);
        return character;
    }

    public async Task<Character?> UpdateAsync(Character character)
    {
        var result = await _characters.ReplaceOneAsync(c => c.Id == character.Id, character);

        if (result.ModifiedCount > 0)
        {
            return character;
        }

        return null;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var result = await _characters.DeleteOneAsync(c => c.Id == id);
        return result.DeletedCount > 0;
    }

    public async Task<IEnumerable<Character>> GetByIds(IEnumerable<string> ids)
    {
        var filter = Builders<Character>.Filter.In(c => c.Id, ids);
        var characters = await _characters.Find(filter).ToListAsync();
        return characters;
    }
}
