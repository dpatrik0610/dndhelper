using dndhelper.Database;
using dndhelper.Models;
using MongoDB.Driver;
using Serilog;

namespace dndhelper.Repositories
{
    public class MusicRepository
    {
        public IMongoCollection<MusicCollection> Collections { get; }
        public IMongoCollection<MusicTrack> Tracks { get; }
        public IMongoCollection<AmbienceLayer> Layers { get; }
        public IMongoCollection<MusicPlayer> Players { get; }

        public MusicRepository(MongoDbContext context, ILogger logger)
        {
            Collections = context.GetCollection<MusicCollection>("MusicCollections");
            Tracks = context.GetCollection<MusicTrack>("MusicTracks");
            Layers = context.GetCollection<AmbienceLayer>("AmbienceLayers");
            Players = context.GetCollection<MusicPlayer>("MusicPlayers");

            try
            {
                Collections.Indexes.CreateOne(new CreateIndexModel<MusicCollection>(Builders<MusicCollection>.IndexKeys.Ascending(c => c.CampaignId)));
                Tracks.Indexes.CreateOne(new CreateIndexModel<MusicTrack>(Builders<MusicTrack>.IndexKeys.Ascending(t => t.CampaignId).Ascending(t => t.CollectionId)));
                Tracks.Indexes.CreateOne(new CreateIndexModel<MusicTrack>(Builders<MusicTrack>.IndexKeys.Ascending(t => t.CampaignId).Ascending(t => t.Hash)));
                Layers.Indexes.CreateOne(new CreateIndexModel<AmbienceLayer>(Builders<AmbienceLayer>.IndexKeys.Ascending(l => l.CampaignId).Ascending(l => l.CollectionId)));
                Players.Indexes.CreateOne(new CreateIndexModel<MusicPlayer>(Builders<MusicPlayer>.IndexKeys.Ascending(p => p.CampaignId), new CreateIndexOptions { Unique = true }));
            }
            catch (MongoException ex)
            {
                logger.Warning(ex, "Could not create music indexes");
            }
        }
    }
}
