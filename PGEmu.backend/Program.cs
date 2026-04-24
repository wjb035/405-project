
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using PGEmuBackend.Data;
using PGEmuBackend.Models;
using PGEmuBackend.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.JsonWebTokens;
using PGEmuBackend.Hubs;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();


// EFCORE
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("Default"),
        new MySqlServerVersion(new Version(8, 0, 0))
    )
);
builder.Services.AddScoped<DevelopmentDataSeeder>();

// Pasword hasher
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();

// JWT Service
builder.Services.AddScoped<JwtService>();

// PFP Storage
builder.Services.Configure<Storage>(
    builder.Configuration.GetSection("Storage"));
builder.Services.AddScoped<AvatarService>();

// Email Service
builder.Services.AddScoped<EmailService>();

// Friends Service
builder.Services.AddScoped<FriendService>();

// Profile customization service

builder.Services.AddScoped<IProfileCustomizationService, ProfileCustomizationService>();
builder.Services.AddScoped<IUserActivityService, UserActivityService>();

// Chat service
builder.Services.AddSignalR();

// user game stuff 
builder.Services.AddScoped<IUserGameService, UserGameService>();
// JWT Authentication
var key = Encoding.UTF8.GetBytes(builder.Configuration["JwtSettings:Secret"]);
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
    .AddJwtBearer(options =>
    {

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
            ValidAudience = builder.Configuration["JwtSettings:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(key),
            ClockSkew = TimeSpan.Zero,

        };
        options.MapInboundClaims = false;
    });

// CORS for godot
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});


var app = builder.Build();

// Apply migrations at startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    var shouldSeedDevelopmentData = builder.Configuration.GetValue("DevelopmentSeed:Enabled", app.Environment.IsDevelopment());
    if (shouldSeedDevelopmentData)
    {
        var developmentSeeder = scope.ServiceProvider.GetRequiredService<DevelopmentDataSeeder>();
        await developmentSeeder.SeedAsync();
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}


app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapHub<ChatHub>("/chathub");

app.UseHttpsRedirection();

app.UseCors("AllowAll");

// For the avatar storage folder
var avatarPath = Path.Combine(
    Directory.GetCurrentDirectory(),
    builder.Configuration["Storage:AvatarPath"]!);

Directory.CreateDirectory(avatarPath);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(avatarPath),
    RequestPath = "/uploads/avatars"
});


app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();
