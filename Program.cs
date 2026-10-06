using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MyLife.Shared.Data;
using MyLife.Features.Auth.Services;
using MyLife.Features.Avatar.Services;
using MyLife.Features.FamilyTree.Services;
using MyLife.Features.Library.Services;
using MyLife.Shared.Security;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình định dạng Log Console kèm Timestamp [HH:mm:ss]
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = false;
    options.SingleLine = true;
    options.TimestampFormat = "[HH:mm:ss] ";
});

// Cấu hình PostgreSQL DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .ToDictionary(entry => entry.Key, entry => entry.Value!.Errors.Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Invalid value." : error.ErrorMessage));
        var message = errors.Values.SelectMany(x => x).FirstOrDefault() ?? "Request validation failed.";
        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { success = false, message, errors });
    };
});
builder.Services.AddHttpClient(nameof(GoogleDriveAvatarService), client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient(nameof(GoogleAvatarSyncService), client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IGoogleCredentialVerifier, GoogleCredentialVerifier>();
builder.Services.AddScoped<IGoogleDriveAvatarService, GoogleDriveAvatarService>();
builder.Services.AddScoped<IGoogleAvatarSyncService, GoogleAvatarSyncService>();
builder.Services.AddScoped<IFamilyTreeService, FamilyTreeService>();
builder.Services.AddHttpClient(nameof(GoogleDriveMemberAvatarService), client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<IGoogleDriveMemberAvatarService, GoogleDriveMemberAvatarService>();
builder.Services.AddHttpClient(nameof(GoogleDriveLibraryStorageService), client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<ILibraryStorageService, GoogleDriveLibraryStorageService>();
builder.Services.AddScoped<ILibraryService, LibraryService>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? (builder.Environment.IsDevelopment() ? ["http://localhost:7000"] : []);
if (allowedOrigins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
    uri.Scheme is not ("http" or "https") || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) ||
    !string.IsNullOrEmpty(uri.Fragment) || origin.Contains('*')))
    throw new InvalidOperationException("Cors:AllowedOrigins must contain exact HTTP(S) origins.");
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "LoginApp API", Version = "v1" });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Nhập Token vào đây:"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Giữ nguyên phần JWT Authentication phía dưới...
var jwtSettings = builder.Configuration.GetSection("JwtSettings");
var jwtSecret = jwtSettings["SecretKey"];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
    throw new InvalidOperationException("JwtSettings:SecretKey must be supplied through environment variables or user secrets and be at least 32 characters.");
if (jwtSettings.GetValue<int>("AccessTokenSeconds") <= 0 || jwtSettings.GetValue<int>("RefreshTokenMinutes") <= 0)
    throw new InvalidOperationException("JwtSettings token lifetimes must be positive.");
if (string.IsNullOrWhiteSpace(jwtSettings["Issuer"]) || string.IsNullOrWhiteSpace(jwtSettings["Audience"]))
    throw new InvalidOperationException("JwtSettings:Issuer and Audience are required.");
var secretKey = Encoding.UTF8.GetBytes(jwtSecret);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(secretKey),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            if (!context.Request.Headers.ContainsKey("Authorization") && context.Request.Cookies.ContainsKey("AccessToken"))
            {
                context.Token = context.Request.Cookies["AccessToken"];
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
            var email = context.Principal?.FindFirstValue(ClaimTypes.Email);
            var user = await db.Users.AsNoTracking().Include(x => x.UserRoles).ThenInclude(x => x.Role)
                .SingleOrDefaultAsync(x => x.Email == email);
            if (user is null || !user.IsActive)
            {
                context.Fail("This account is unavailable.");
                return;
            }
            // Enforce current DB roles, including demotions, even before an old access token expires.
            if (context.Principal?.Identity is ClaimsIdentity identity)
            {
                foreach (var claim in identity.FindAll(identity.RoleClaimType).ToList()) identity.RemoveClaim(claim);
                foreach (var role in user.UserRoles.Select(x => x.Role.Name).Where(x => x is AppRoles.Admin or AppRoles.User).Distinct())
                    identity.AddClaim(new Claim(identity.RoleClaimType, role));
            }
        },
        OnChallenge = async context =>
        {
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { success = false, message = "A valid session is required." });
        },
        OnForbidden = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return context.Response.WriteAsJsonAsync(new { success = false, message = "You do not have permission to perform this action." });
        }
    };
});

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
    var databaseConflict = exception is Npgsql.PostgresException { SqlState: "40001" or "40P01" or "23505" }
        || exception is DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "40001" or "40P01" or "23505" } };
    context.Response.StatusCode = databaseConflict ? StatusCodes.Status409Conflict : StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { success = false, message = databaseConflict ? "The request conflicted with another change. Please retry." : "An unexpected server error occurred." });
}));

// A failed schema update must stop startup rather than serving a partially initialized database.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseStartup.MigrateAndSeedAsync(db, app.Configuration);
    app.Logger.LogInformation("Database migrations and seed completed.");
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseCors("AllowReactApp");

// Browser cookie requests use a custom header to prevent form-based CSRF.
// Cross-origin browser calls must also originate from an explicitly trusted origin.
app.Use(async (context, nextMiddleware) =>
{
    var unsafeMethod = context.Request.Method is not ("GET" or "HEAD" or "OPTIONS");
    if (unsafeMethod)
    {
        var origin = context.Request.Headers.Origin.ToString();
        var hasCookieSession = context.Request.Cookies.ContainsKey("AccessToken") || context.Request.Cookies.ContainsKey("RefreshToken");
        if ((!string.IsNullOrEmpty(origin) && !allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)) ||
            (hasCookieSession && context.Request.Headers["X-Requested-With"] != "MyLife"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { success = false, message = "The request origin could not be verified." });
            return;
        }
    }
    await nextMiddleware(context);
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program { }
