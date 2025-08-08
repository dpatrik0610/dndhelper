using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories.Interfaces;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dndhelper.Repositories
{
    public class InventoryRepository : IInventoryRepository
    {
        private readonly IMongoCollection<Inventory> _collection;

        public InventoryRepository(MongoDbContext context)
        {
            _collection = context.GetCollection<Inventory>("Inventories");
        }

        // Inventory CRUD

        public async Task<IEnumerable<Inventory>> GetByCharacterIdAsync(string characterId) =>
            await _collection.Find(i => i.CharacterId == characterId).ToListAsync();

        public async Task<Inventory?> GetByIdAsync(string id) =>
            await _collection.Find(i => i.Id == id).FirstOrDefaultAsync();

        public async Task<Inventory> AddAsync(Inventory inventory)
        {
            await _collection.InsertOneAsync(inventory);
            return inventory;
        }

        public async Task<Inventory?> UpdateAsync(Inventory inventory)
        {
            var filter = Builders<Inventory>.Filter.Eq(x => x.Id, inventory.Id);
            var result = await _collection.ReplaceOneAsync(filter, inventory);

            if (result.MatchedCount == 0)
                return null;

            return inventory;
        }

        public async Task DeleteAsync(string id) =>
            await _collection.DeleteOneAsync(i => i.Id == id);

        // InventoryItem CRUD

        public async Task<IEnumerable<InventoryItem>> GetItemsAsync(string inventoryId)
        {
            var inventory = await _collection.Find(i => i.Id == inventoryId).FirstOrDefaultAsync();
            return inventory?.Items ?? Enumerable.Empty<InventoryItem>();
        }

        public async Task<InventoryItem?> GetItemAsync(string inventoryId, string equipmentIndex)
        {
            var inventory = await _collection.Find(i => i.Id == inventoryId).FirstOrDefaultAsync();
            return inventory?.Items.FirstOrDefault(item => item.EquipmentIndex == equipmentIndex);
        }

        public async Task<InventoryItem?> AddItemAsync(string inventoryId, InventoryItem item)
        {
            if (!ObjectId.TryParse(inventoryId, out var objId))
                return null;

            var filter = Builders<Inventory>.Filter.Eq("_id", objId);
            var update = Builders<Inventory>.Update.Push(i => i.Items, item);

            var result = await _collection.UpdateOneAsync(filter, update);
            if (result.ModifiedCount == 0)
                return null;

            // Return the added item by querying the inventory again
            var inventory = await _collection.Find(filter).FirstOrDefaultAsync();
            return inventory?.Items?.Find(i => i.EquipmentIndex == item.EquipmentIndex);
        }


        public async Task UpdateItemAsync(string inventoryId, InventoryItem item)
        {
            var filter = Builders<Inventory>.Filter.And(
                Builders<Inventory>.Filter.Eq(i => i.Id, inventoryId),
                Builders<Inventory>.Filter.ElemMatch(i => i.Items, x => x.EquipmentIndex == item.EquipmentIndex)
            );

            var update = Builders<Inventory>.Update
                .Set("Items.$.Quantity", item.Quantity)
                .Set("Items.$.Note", item.Note);

            await _collection.UpdateOneAsync(filter, update);
        }

        public async Task DeleteItemAsync(string inventoryId, string equipmentIndex)
        {
            var update = Builders<Inventory>.Update.PullFilter(i => i.Items, x => x.EquipmentIndex == equipmentIndex);
            await _collection.UpdateOneAsync(i => i.Id == inventoryId, update);
        }
    }
}
