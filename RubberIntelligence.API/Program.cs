using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using RubberIntelligence.API.Auth.Jwt;
using RubberIntelligence.API.Data;
using RubberIntelligence.API.Data.Repositories;
using RubberIntelligence.API.Data.Seed;
using RubberIntelligence.API.Infrastructure.Security;
using RubberIntelligence.API.Modules.DiseaseDetection.Models;
using RubberIntelligence.API.Modules.DiseaseDetection.Services;
using System.Text;
using DotNetEnv;

var builder = WebApplication.CreateBuilder(args);

// Load .env file
Env.Load();

// Replace configuration with Environment Variables if they exist
builder.Configuration.AddEnvironmentVariables();


// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure JWT Settings
var jwtSection = builder.Configuration.GetSection(JwtSettings.SectionName);
// Configure JWT Settings (Centralized to ensure consistency)
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY") ?? jwtSection["Key"]!;
var jwtIssuer = jwtSection["Issuer"]!;
var jwtAudience = jwtSection["Audience"]!;
var jwtExpiryMinutes = int.Parse(jwtSection["ExpiryMinutes"]!);

builder.Services.Configure<JwtSettings>(options =>
{
    options.Key = jwtKey;
    options.Issuer = jwtIssuer;
    options.Audience = jwtAudience;
    options.ExpiryMinutes = jwtExpiryMinutes;
});

var jwtSettings = jwtSection.Get<JwtSettings>();

// Configure MongoDB Settings
builder.Services.Configure<MongoDbSettings>(options =>
{
    options.ConnectionString = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING") ?? builder.Configuration.GetSection("MongoDbSettings:ConnectionString").Value!;
    options.DatabaseName = builder.Configuration.GetSection("MongoDbSettings:DatabaseName").Value!;
});

// Configure Alert Settings
builder.Services.Configure<AlertSettings>(builder.Configuration.GetSection(AlertSettings.SectionName));

// Register Data Services
builder.Services.AddSingleton<AppDbContext>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IDppRepository, DppRepository>();
builder.Services.AddScoped<IMarketplaceRepository, MarketplaceRepository>();
builder.Services.AddTransient<DbSeeder>();

// Register Module Services
// Register Disease Detection Strategy — API-based services
builder.Services.AddHttpClient<RubberIntelligence.API.Modules.DiseaseDetection.Services.PlantNetWeedService>(); // Type-Client
builder.Services.AddHttpClient<RubberIntelligence.API.Modules.DiseaseDetection.Services.PlantIdDiseaseService>(); // Plant.id API
builder.Services.AddHttpClient<RubberIntelligence.API.Modules.DiseaseDetection.Services.InsectIdPestService>(); // Insect.id API
builder.Services.AddScoped<RubberIntelligence.API.Modules.DiseaseDetection.Services.PlantIdDiseaseService>();
builder.Services.AddScoped<RubberIntelligence.API.Modules.DiseaseDetection.Services.InsectIdPestService>();
builder.Services.AddScoped<RubberIntelligence.API.Modules.DiseaseDetection.Services.PlantNetWeedService>();
// Register Image Validation Services
builder.Services.AddScoped<RubberIntelligence.API.Modules.DiseaseDetection.Services.ImageQualityService>();
builder.Services.AddSingleton<RubberIntelligence.API.Modules.DiseaseDetection.Services.ContentVerificationService>();
builder.Services.AddScoped<RubberIntelligence.API.Modules.DiseaseDetection.Services.IImageValidationService, RubberIntelligence.API.Modules.DiseaseDetection.Services.ImageValidationService>();
builder.Services.AddScoped<RubberIntelligence.API.Modules.DiseaseDetection.Services.IDiseaseDetectionService, RubberIntelligence.API.Modules.DiseaseDetection.Services.CompositeDiseaseService>();
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<RubberIntelligence.API.Modules.PriceForecasting.Services.IPriceForecastingService, RubberIntelligence.API.Modules.PriceForecasting.Services.OnnxPriceForecastingService>();
builder.Services.AddScoped<RubberIntelligence.API.Modules.Grading.Services.IGradingService, RubberIntelligence.API.Modules.Grading.Services.OnnxGradingService>();
builder.Services.AddScoped<RubberIntelligence.API.Modules.RubberLatexQuality.Services.ILatexQualityService, RubberIntelligence.API.Modules.RubberLatexQuality.Services.OnnxLatexQualityService>();

builder.Services.AddHttpClient<RubberIntelligence.API.Modules.Dpp.Services.GeminiOcrService>();
builder.Services.AddSingleton<RubberIntelligence.API.Modules.Dpp.Services.OnnxDppService>();
builder.Services.AddScoped<RubberIntelligence.API.Modules.Dpp.Services.FieldEncryptionService>();
builder.Services.AddScoped<RubberIntelligence.API.Modules.Dpp.Services.FieldConfidentialityService>();
builder.Services.AddScoped<RubberIntelligence.API.Modules.Dpp.Services.DppDocumentProcessingService>(); // Fix 2: clean architecture
builder.Services.AddScoped<RubberIntelligence.API.Modules.Dpp.Services.DppService>();
builder.Services.AddScoped<RubberIntelligence.API.Modules.Dpp.Services.DppEncryptionService>(); // File-level AES encryption
builder.Services.AddScoped<RubberIntelligence.API.Modules.Dpp.Services.ConfidentialAccessService>(); // Controlled access decryption
builder.Services.AddScoped<RubberIntelligence.API.Modules.Dpp.Services.ExporterContextService>(); // Exporter profile context for buyers
builder.Services.AddScoped<RubberIntelligence.API.Modules.Dpp.Services.MessageService>(); // Lot-linked secure messaging
builder.Services.AddScoped<RubberIntelligence.API.Modules.Marketplace.Services.BuyerHistoryService>(); // Buyer history analytics
builder.Services.AddScoped<RubberIntelligence.API.Data.Repositories.IMessageRepository, RubberIntelligence.API.Data.Repositories.MessageRepository>(); // Message persistence

// Register Infrastructure Services
builder.Services.AddScoped<JwtTokenService>();

// Configure JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey))
        };
    });

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        builder => builder
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
});

var app = builder.Build();

// Global exception handler for MongoDB connectivity issues
// Must be added early so it wraps the rest of the pipeline
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (TimeoutException ex) when (ex.Message.Contains("selecting a server"))
    {
        context.Response.StatusCode = 503;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                error = "Database connection timed out.",
                hint = "Check MongoDB Atlas: whitelist your IP in Network Access, and ensure the cluster is not paused."
            }));
    }
    catch (MongoDB.Driver.MongoException ex)
    {
        context.Response.StatusCode = 503;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                error = "Database error. Please try again later.",
                detail = ex.Message
            }));
    }
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Ensure geospatial indexes + Seed Database
// Wrapped in try-catch: a MongoDB timeout must not crash the whole app
using (var scope = app.Services.CreateScope())
{
    try
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.EnsureIndexesAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
        await seeder.SeedAsync();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "⚠️  MongoDB startup tasks failed (indexes/seed). " +
            "Check your Atlas IP whitelist or network. The API will still start.");
    }
}

app.Run();
