using dndhelper.Authentication;
using dndhelper.Authentication.Interfaces;
using dndhelper.Authorization.Policies;
using dndhelper.Database;
using dndhelper.Repositories;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services;
using dndhelper.Services.CharacterServices.Interfaces;
using dndhelper.Services.Interfaces;
using dndhelper.Utils;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System;
using System.Text;

namespace dndhelper.Core
{
    public static class ServiceExtensions
    {
        public static IServiceCollection InjectServices(this IServiceCollection services, IConfiguration config)
        {
            // Caching
            services.AddMemoryCache();

            // Logger
            services.AddSingleton(CustomLogger.CreateLogger());
            var logger = services.BuildServiceProvider().GetRequiredService<ILogger>();

            // Database setup
            services.AddSingleton(sp =>
            {
                var connectionString = config.GetValue<string>("MongoDB:ConnectionString");
                var databaseName = config.GetValue<string>("MongoDB:DatabaseName");

                if (string.IsNullOrEmpty(databaseName))
                    throw new ArgumentException($"'{nameof(databaseName)}' cannot be null or empty.", nameof(databaseName));
                if (string.IsNullOrEmpty(connectionString))
                    throw new ArgumentException($"'{nameof(connectionString)}' cannot be null or empty.", nameof(connectionString));

                return new MongoDbContext(connectionString, databaseName, logger);
            });

            // Authentication
            services.AddHttpContextAccessor();
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = config["Jwt:Issuer"],
                    ValidAudience = config["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!))
                };
            });

            // Authorization
            services.AddAuthorization(options =>
            {
                options.AddPolicy("OwnershipPolicy", policy =>
                    policy.Requirements.Add(new OwnershipRequirement()));
            });
            services.AddSingleton<IAuthorizationHandler, OwnershipHandler>();

            // Repos
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<ICharacterRepository, CharacterRepository>();
            services.AddScoped<IEquipmentRepository, EquipmentRepository>();
            services.AddScoped<IInventoryRepository, InventoryRepository>();
            services.AddScoped<IMonsterRepository, MonsterRepository>();
            services.AddScoped<ICampaignRepository, CampaignRepository>();

            // Services
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IDiceRollService, DiceRollService>();
            services.AddScoped<ICharacterService, CharacterService>();
            services.AddScoped<IEquipmentService, EquipmentService>();
            services.AddScoped<IInventoryService, InventoryService>();
            services.AddScoped<IMonsterService, MonsterService>();
            services.AddScoped<ICampaignService, CampaignService>();

            var dndApiUrl = config.GetValue<string>("DndApi:BaseUrl") ?? throw CustomExceptions.ThrowArgumentNullException(logger, "Logger");
            services.AddHttpClient<IPublicDndApiClient, PublicDndApiClient>(client =>
            {
                client.BaseAddress = new Uri(dndApiUrl);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            // Utils
            return services;
        }
    }
}