using dndhelper.Authentication;
using dndhelper.Authentication.Interfaces;
using dndhelper.Database;
using dndhelper.Models;
using dndhelper.Repositories;
using dndhelper.Repositories.Interfaces;
using dndhelper.Services;
using dndhelper.Services.Auth;
using dndhelper.Services.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
            // Logger
            services.AddSingleton(CustomLogger.CreateLogger());

            // Database setup
            services.AddSingleton(sp =>
            {
                var connectionString = config.GetValue<string>("MongoDB:ConnectionString");
                var databaseName = config.GetValue<string>("MongoDB:DatabaseName");
                var logger = sp.GetRequiredService<ILogger>();

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
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]))
                };
            });

            // Repos
            services.AddScoped<ICharacterRepository, CharacterRepository>();
            services.AddScoped<IEquipmentRepository, EquipmentRepository>();
            services.AddScoped<IInventoryRepository, InventoryRepository>();

            // Services
            services.AddScoped<IDiceRollService, DiceRollService>();
            services.AddScoped<ICharacterService, CharacterService>();
            services.AddScoped<IEquipmentService, EquipmentService>();
            services.AddScoped<IInventoryService, InventoryService>();
            services.AddHttpClient<IPublicDndApiClient, PublicDndApiClient>(client =>
            {
                client.BaseAddress = new Uri("https://www.dnd5eapi.co/api/2014/");
                client.DefaultRequestHeaders.Add("Accept", "application/json");
            });

            // Utils
            return services;
        }
    }
}