using dndhelper.Authentication;
using dndhelper.Authentication.Interfaces;
using dndhelper.Authorization;
using dndhelper.Authorization.Policies;
using dndhelper.Database;
using dndhelper.Database.Seed;
using dndhelper.Models;
using dndhelper.Models.CharacterModels;
using dndhelper.Models.RollModels;
using dndhelper.Repositories;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services;
using dndhelper.Services.CharacterServices.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Services.SignalR;
using dndhelper.Utils;
using Amazon.Runtime;
using Amazon.S3;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson.Serialization.Conventions;
using Serilog;
using System;
using System.Text;

namespace dndhelper.Core
{
    public static class ServiceExtensions
    {
        public static IServiceCollection InjectServices(this IServiceCollection services, IConfiguration config)
        {
            // One logger for everything: injected ILogger, static Log.* and UseSerilog() (which reads Log.Logger).
            var logger = Log.Logger = CustomLogger.CreateLogger();
            services.AddSingleton(logger);

            #region Caching
            services.AddMemoryCache();
            services.AddSingleton<IMemoryCache>(sp =>
            {
                var inner = new MemoryCache(new MemoryCacheOptions());
                return new TrackingMemoryCache(inner);
            });
            services.AddScoped<ICacheService, CacheService>();
            #endregion

            #region Database
            // Unknown fields are ignored on read, so adding or dropping model properties never breaks old documents.
            ConventionRegistry.Register("IgnoreExtraElements",
                new ConventionPack { new IgnoreExtraElementsConvention(true) }, _ => true);

            services.AddSingleton(sp =>
            {
                var connectionString = config["MongoDB:ConnectionString"];
                var databaseName = config["MongoDB:DatabaseName"];

                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    logger.Error("MongoDB connection string is missing! Check your environment variables (MongoDB__ConnectionString).");
                    throw new InvalidOperationException("MongoDB connection string not found.");
                }
                if (string.IsNullOrWhiteSpace(databaseName))
                {
                    logger.Error("MongoDB database name is missing! Check your environment variables (MongoDB__DatabaseName).");
                    throw new InvalidOperationException("MongoDB database name not found.");
                }

                logger.Information("MongoDB configured for database {DbName}", databaseName);
                var context = new MongoDbContext(connectionString, databaseName, logger);

                try
                {
                    RuleCategoriesSeeder.SeedDefaultCategoriesAsync(context, logger).GetAwaiter().GetResult();
                    RulesSeeder.SeedSampleRulesAsync(context, logger).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Failed to seed default data into MongoDB.");
                }

                return context;
            });
            services.AddHealthChecks()
                    .AddCheck<MongoHealthCheck>("mongodb");
            #endregion

            #region Auth
            // Secret: only from the environment (Jwt__Key), never from appsettings. HS256 needs at least 256 bits.
            var jwtKey = config["Jwt:Key"];
            if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
            {
                logger.Error("JWT signing key is missing or shorter than 32 bytes. Set the Jwt__Key environment variable.");
                throw new InvalidOperationException("JWT signing key not configured (Jwt__Key).");
            }

            services.AddHttpContextAccessor();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddSingleton<LoginThrottle>();
            services.AddAuthRateLimits();
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;

                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                        {
                            context.Token = accessToken;
                        }

                        return System.Threading.Tasks.Task.CompletedTask;
                    }
                };

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = config["Jwt:Issuer"],
                    ValidAudience = config["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
                };
            });

            services.AddAuthorization(options =>
            {
                options.AddPolicy("OwnershipPolicy", policy =>
                    policy.Requirements.Add(new OwnershipRequirement()));
            });
            services.AddScoped<CampaignAccess>();
            services.AddScoped<IAuthorizationHandler, OwnershipHandler>();
            #endregion

            #region Repos
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<ICharacterRepository, CharacterRepository>();
            services.AddScoped<IEquipmentRepository, EquipmentRepository>();
            services.AddScoped<IInventoryRepository, InventoryRepository>();
            services.AddScoped<IMonsterRepository, MonsterRepository>();
            services.AddScoped<ICampaignRepository, CampaignRepository>();
            services.AddScoped<IEncounterRepository, EncounterRepository>();
            services.AddScoped<ISpellRepository, SpellRepository>();
            services.AddScoped<INoteRepository, NoteRepository>();
            services.AddScoped<ISessionRepository, SessionRepository>();
            services.AddScoped<IRuleRepository, RuleRepository>();
            services.AddScoped<IRuleCategoryRepository, RuleCategoryRepository>();
            services.AddScoped<IRollRepository, RollRepository>();
            services.AddSingleton<ITabletopRepository, TabletopRepository>();
            services.AddSingleton<IChatRepository, ChatRepository>();
            services.AddScoped<IShopRepository, ShopRepository>();
            services.AddScoped<ISellRequestRepository, SellRequestRepository>();
            services.AddScoped<IQuestRepository, QuestRepository>();
            services.AddScoped<IPollRepository, PollRepository>();
            services.AddScoped<IStoredFileRepository, StoredFileRepository>();
            #endregion

            #region Services
            services.AddScoped<IEntitySyncService, EntitySyncService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IDiceRollService, DiceRollService>();
            services.AddScoped<ISubtleRollService, SubtleRollService>();
            services.AddScoped<IRollHistoryService, RollHistoryService>();
            services.AddScoped<ICharacterService, CharacterService>();
            services.AddScoped<IEquipmentService, EquipmentService>();
            services.AddScoped<IInventoryService, InventoryService>();
            services.AddScoped<IMonsterService, MonsterService>();
            services.AddScoped<ICampaignService, CampaignService>();
            services.AddScoped<IEncounterService, EncounterService>();
            services.AddScoped<ISpellService, SpellService>();
            services.AddScoped<ICurrencyService, CurrencyService>();
            services.AddScoped<INoteService, NoteService>();
            services.AddScoped<ISessionService, SessionService>();
            services.AddScoped<IBackupService, BackupService>();
            services.AddScoped<Database.Migrations.CampaignScopeMigration>();
            services.AddScoped<Database.Migrations.MonsterModelMigration>();
            services.AddScoped<IRuleService, RuleService>();
            services.AddScoped<IRuleCategoryService, RuleCategoryService>();
            services.AddScoped<ITabletopService, TabletopService>();
            services.AddSingleton<TablePresence>();
            services.AddScoped<IChatService, ChatService>();
            services.AddScoped<IShopService, ShopService>();
            services.AddScoped<ILootService, LootService>();
            services.AddScoped<IQuestService, QuestService>();
            services.AddScoped<IPollService, PollService>();

            services.AddScoped<IInternalBaseService<Inventory>, InventoryService>();
            services.AddScoped<IInternalBaseService<Character>, CharacterService>();
            services.AddScoped<IInternalBaseService<Encounter>, EncounterService>();
            services.AddScoped<IInternalBaseService<Session>, SessionService>();
            services.AddScoped<IInternalBaseService<Quest>, QuestService>();
            #endregion

            services.Configure<DiceRollOptions>(config.GetSection("DiceRoll"));

            #region File storage (Cloudflare R2)
            services.Configure<R2Options>(config.GetSection("R2"));
            // Built on first use, so the app still starts without R2 configured; uploads then fail with this message.
            services.AddSingleton<IAmazonS3>(sp =>
            {
                var o = sp.GetRequiredService<IOptions<R2Options>>().Value;
                if (string.IsNullOrWhiteSpace(o.Endpoint) || string.IsNullOrWhiteSpace(o.AccessKeyId)
                    || string.IsNullOrWhiteSpace(o.SecretAccessKey) || string.IsNullOrWhiteSpace(o.BucketName))
                    throw new InvalidOperationException("R2 storage isn't configured (R2__Endpoint, R2__AccessKeyId, R2__SecretAccessKey, R2__BucketName).");

                return new AmazonS3Client(new BasicAWSCredentials(o.AccessKeyId, o.SecretAccessKey), new AmazonS3Config
                {
                    ServiceURL = o.Endpoint,
                    AuthenticationRegion = "auto",
                    // R2 rejects the SDK's newer default checksum headers
                    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                });
            });
            services.AddSingleton(sp => new Lazy<IAmazonS3>(sp.GetRequiredService<IAmazonS3>));
            services.AddScoped<IFileStorageService, FileStorageService>();
            #endregion

            var dndApiUrl = config.GetValue<string>("DndApi:BaseUrl") ?? throw CustomExceptions.ThrowArgumentNullException(logger, "Logger");
            services.AddHttpClient<IPublicDndApiClient, PublicDndApiClient>(client =>
            {
                client.BaseAddress = new Uri(dndApiUrl);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            return services;
        }
    }
}
