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
using MyLife.Shared.Web;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);

// Cấu hình định dạng Log Console kèm Timestamp [HH:mm:ss]
builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = true;
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
        if (context.HttpContext.Request.Method == "POST" &&
            context.HttpContext.Request.Path.Equals("/api/auth/register", StringComparison.OrdinalIgnoreCase))
        {
            string Attempted(string key) => context.ModelState.TryGetValue(key, out var entry) ? entry.AttemptedValue ?? string.Empty : string.Empty;
            string? code = null;
            string? codedMessage = null;
            if (!string.IsNullOrWhiteSpace(Attempted("ConfirmPassword")) &&
                !string.IsNullOrWhiteSpace(Attempted("Password")) &&
                !string.Equals(Attempted("ConfirmPassword"), Attempted("Password"), StringComparison.Ordinal))
            {
                code = "AUTH_PASSWORD_MISMATCH"; codedMessage = "Passwords do not match.";
            }
            else if (System.Text.Encoding.UTF8.GetByteCount(Attempted("Password")) > 72)
            {
                code = "AUTH_PASSWORD_TOO_LONG"; codedMessage = "Password must not exceed 72 UTF-8 bytes.";
            }
            else if (!string.IsNullOrWhiteSpace(Attempted("Email")) &&
                !Attempted("Email").EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase))
            {
                code = "AUTH_GMAIL_REQUIRED"; codedMessage = "Only Gmail addresses are supported.";
            }
            else if (context.ModelState.TryGetValue("DateOfBirth", out var dob) && dob.Errors.Count > 0)
            {
                code = "AUTH_DOB_INVALID"; codedMessage = "Date of birth is invalid.";
            }

            if (code is not null)
                return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new { success = false, code, message = codedMessage });
        }
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
builder.Services.AddScoped<IFamilyRelationshipService, FamilyRelationshipService>();
builder.Services.AddScoped<IFamilyTimelineService, FamilyTimelineService>();
builder.Services.AddScoped<IFamilyMemberProfileService, FamilyMemberProfileService>();
builder.Services.Configure<GeocodingOptions>(builder.Configuration.GetSection("Geocoding"));
builder.Services.AddHttpClient<IGeocodingService, NominatimGeocodingService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.UserAgent.ParseAdd(builder.Configuration["Geocoding:UserAgent"] ?? "MyLife/1.0 (family-tree-project)");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("vi");
});
builder.Services.AddSingleton<FamilyLocationGeocodingQueue>();
builder.Services.AddSingleton<IFamilyLocationGeocodingQueue>(provider => provider.GetRequiredService<FamilyLocationGeocodingQueue>());
builder.Services.AddHostedService(provider => provider.GetRequiredService<FamilyLocationGeocodingQueue>());
builder.Services.AddScoped<FamilyMapService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<VietnameseLunarCalendarService>();
builder.Services.AddScoped<IFamilyAnniversaryService, FamilyAnniversaryService>();
builder.Services.AddMemoryCache(options => options.SizeLimit = 64L * 1024 * 1024);
builder.Services.AddSingleton<IMemberAvatarImageCache, MemberAvatarImageCache>();
builder.Services.AddSingleton<ILibraryPhotoImageCache, LibraryPhotoImageCache>();
builder.Services.AddHttpClient(nameof(GoogleDriveMemberAvatarService), client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<IGoogleDriveMemberAvatarService, GoogleDriveMemberAvatarService>();
builder.Services.AddHttpClient(nameof(GoogleDriveLibraryStorageService), client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<ILibraryStorageService, GoogleDriveLibraryStorageService>();
builder.Services.AddScoped<ILibraryService, LibraryService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

builder.Services.AddWebRateLimits(builder.Configuration);
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgres", tags: ["ready"]);
builder.Services.AddResponseCompression(options => { options.EnableForHttps = true; options.Providers.Add<BrotliCompressionProvider>(); options.Providers.Add<GzipCompressionProvider>(); });

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
              .AllowCredentials()
              .WithExposedHeaders("X-Request-ID", "Retry-After");
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
            var user = await db.Users.AsNoTracking().Where(x => x.Email == email)
                .Select(x => new { x.Id, x.IsActive, Roles = x.UserRoles.Select(r => r.Role.Name).ToArray() })
                .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
            if (user is null || !user.IsActive)
            {
                context.Fail("This account is unavailable.");
                return;
            }
            var role = AppRoles.ExclusiveRole(user.Roles);
            if (role is null) { context.Fail("This account role is unavailable."); return; }
            context.HttpContext.Items[CurrentUserService.VerifiedUserIdKey] = user.Id;
            // Enforce current DB roles, including demotions, even before an old access token expires.
            if (context.Principal?.Identity is ClaimsIdentity identity)
            {
                foreach (var claim in identity.FindAll(identity.RoleClaimType).ToList()) identity.RemoveClaim(claim);
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

app.UseMiddleware<RequestContextMiddleware>();
// Compress data responses, excluding token-issuing auth responses and image bytes.
app.UseWhen(http => http.Request.Path.StartsWithSegments("/api/family-tree") || http.Request.Path.StartsWithSegments("/api/library") || http.Request.Path.StartsWithSegments("/health"), branch => branch.UseResponseCompression());

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    logger.LogError("Unhandled exception ErrorType={ErrorType} Method={Method} Path={Path}", exception?.GetType().Name, context.Request.Method, context.Request.Path);
    var databaseConflict = exception is Npgsql.PostgresException { SqlState: "40001" or "40P01" or "23505" or "23503" or "23001" }
        || exception is DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "40001" or "40P01" or "23505" or "23503" or "23001" } };
    context.Response.StatusCode = databaseConflict ? StatusCodes.Status409Conflict : StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new { success = false, message = databaseConflict ? "The request conflicted with another change. Please retry." : "An unexpected server error occurred." });
}));

// A failed schema update must stop startup rather than serving a partially initialized database.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseStartup.InitializeAsync(db, app.Configuration, app.Environment.IsDevelopment());
    app.Logger.LogInformation("Database startup validation completed.");
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
app.UseRateLimiter();

app.MapControllers();
var healthOptions = new HealthCheckOptions
{
    ResponseWriter = (http, report) => http.Response.WriteAsJsonAsync(new { status = report.Status.ToString() })
};
app.MapHealthChecks("/health/ready", healthOptions);
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = healthOptions.ResponseWriter });

app.Run();

public partial class Program { }
