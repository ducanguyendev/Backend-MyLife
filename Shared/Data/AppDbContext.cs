using System;
using System.Threading.Tasks;
using MyLife.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using MyLife.Shared.Security;

namespace MyLife.Shared.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<LoginLog> LoginLogs => Set<LoginLog>();
        public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
        public DbSet<FamilyRelationship> FamilyRelationships => Set<FamilyRelationship>();
        public DbSet<Generation> Generations => Set<Generation>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. Table USERS
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("users");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
                entity.HasIndex(e => e.Email).IsUnique();
                entity.Property(e => e.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
                entity.Property(e => e.FullName).HasColumnName("full_name").HasMaxLength(100);
                entity.Property(e => e.PhoneNumber).HasColumnName("phone_number").HasMaxLength(20);
                entity.Property(e => e.Gender).HasColumnName("gender").HasMaxLength(20);
                entity.Property(e => e.DateOfBirth).HasColumnName("date_of_birth");
                entity.Property(e => e.AvatarUrl).HasColumnName("avatar_url").HasColumnType("text");
                entity.Property(e => e.AuthProvider).HasColumnName("auth_provider").HasDefaultValue(0);
                entity.Property(e => e.HasLocalProvider).HasColumnName("has_local_provider").HasDefaultValue(false);
                entity.Property(e => e.HasGoogleProvider).HasColumnName("has_google_provider").HasDefaultValue(false);
                entity.Property(e => e.GoogleSubject).HasColumnName("google_subject").HasMaxLength(255);
                entity.HasIndex(e => e.GoogleSubject).IsUnique();
                entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
                entity.Property(e => e.VerifiedAt).HasColumnName("verified_at");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // 2. Table ROLES
            modelBuilder.Entity<Role>(entity =>
            {
                entity.ToTable("roles");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
                entity.HasIndex(e => e.Name).IsUnique();
                entity.Property(e => e.Description).HasColumnName("description").HasColumnType("text");
            });

            // 3. Table USER_ROLES (Composite PK)
            modelBuilder.Entity<UserRole>(entity =>
            {
                entity.ToTable("user_roles");
                entity.HasKey(e => new { e.UserId, e.RoleId });
                entity.Property(e => e.UserId).HasColumnName("user_id");
                entity.Property(e => e.RoleId).HasColumnName("role_id");

                entity.HasOne(e => e.User)
                    .WithMany(u => u.UserRoles)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Role)
                    .WithMany(r => r.UserRoles)
                    .HasForeignKey(e => e.RoleId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // 4. Table REFRESH_TOKENS
            modelBuilder.Entity<RefreshToken>(entity =>
            {
                entity.ToTable("refresh_tokens");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.UserId).HasColumnName("user_id");
                entity.Property(e => e.Token).HasColumnName("token").HasColumnType("text").IsRequired();
                entity.HasIndex(e => e.Token).IsUnique();
                entity.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(45);
                entity.Property(e => e.UserAgent).HasColumnName("user_agent").HasColumnType("text");
                entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();
                entity.Property(e => e.IsRevoked).HasColumnName("is_revoked").HasDefaultValue(false);
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.ExpiresAt);

                entity.HasOne(e => e.User)
                    .WithMany(u => u.RefreshTokens)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // 5. Table LOGIN_LOGS
            modelBuilder.Entity<LoginLog>(entity =>
            {
                entity.ToTable("login_logs");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
                entity.Property(e => e.UserId).HasColumnName("user_id");
                entity.Property(e => e.AttemptEmail).HasColumnName("attempt_email").HasMaxLength(255).IsRequired();
                entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
                entity.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(45);
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.HasIndex(e => e.AttemptEmail);
                entity.HasIndex(e => e.CreatedAt);

                entity.HasOne(e => e.User)
                    .WithMany(u => u.LoginLogs)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // 6. Table FAMILY_MEMBERS
            modelBuilder.Entity<FamilyMember>(entity =>
            {
                entity.ToTable("family_members");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                
                entity.HasOne(e => e.Father)
                    .WithMany(e => e.ChildrenAsFather)
                    .HasForeignKey(e => e.FatherId)
                    .OnDelete(DeleteBehavior.SetNull);
                    
                entity.HasOne(e => e.Mother)
                    .WithMany(e => e.ChildrenAsMother)
                    .HasForeignKey(e => e.MotherId)
                    .OnDelete(DeleteBehavior.SetNull);
                    
                entity.HasOne(e => e.Spouse)
                    .WithMany()
                    .HasForeignKey(e => e.SpouseId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            // 7. Table FAMILY_RELATIONSHIPS
            modelBuilder.Entity<FamilyRelationship>(entity =>
            {
                entity.ToTable("family_relationships");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id");
                entity.Property(e => e.Member1Id).HasColumnName("member1_id");
                entity.Property(e => e.Member2Id).HasColumnName("member2_id");
                entity.Property(e => e.RelationType).HasColumnName("relation_type").HasMaxLength(50).IsRequired();
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                
                entity.HasIndex(e => new { e.Member1Id, e.Member2Id, e.RelationType }).IsUnique();

                entity.HasOne(e => e.Member1)
                    .WithMany(m => m.RelationsAsMember1)
                    .HasForeignKey(e => e.Member1Id)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Member2)
                    .WithMany(m => m.RelationsAsMember2)
                    .HasForeignKey(e => e.Member2Id)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // 8. Table GENERATIONS
            modelBuilder.Entity<Generation>(entity =>
            {
                entity.ToTable("generations");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
                entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(50).IsRequired();
                entity.Property(e => e.Title).HasColumnName("title").HasMaxLength(100);
                entity.Property(e => e.Description).HasColumnName("description").HasColumnType("text");
                entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
                entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("CURRENT_TIMESTAMP");
            });
        }

        /// <summary>
        /// Tự động chèn dữ liệu mẫu (Roles, Generations & Duy nhất 1 tài khoản Admin) khi database rỗng
        /// </summary>
        public async Task SeedDataAsync(IConfiguration configuration)
        {
            await using var transaction = await Database.BeginTransactionAsync();
            // Keep the seed idempotent even when multiple API instances start together.
            await Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(741003001);");

            foreach (var roleName in new[] { AppRoles.Admin, AppRoles.User })
            {
                if (!await Roles.AnyAsync(role => role.Name == roleName))
                    Roles.Add(new Role { Name = roleName });
            }
            await SaveChangesAsync();

            for (var generation = 1; generation <= 5; generation++)
            {
                if (!await Generations.AnyAsync(item => item.Id == generation))
                    Generations.Add(new Generation { Id = generation, Name = $"Đời {generation}", Title = $"Thế hệ thứ {generation}" });
            }

            var seedEmail = configuration["SeedAdmin:Email"]?.Trim().ToLowerInvariant();
            var seedPassword = configuration["SeedAdmin:Password"];
            if (!string.IsNullOrWhiteSpace(seedEmail) || !string.IsNullOrWhiteSpace(seedPassword))
            {
                if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(seedEmail))
                    throw new InvalidOperationException("SeedAdmin email must be a valid email address.");

                var validSeedPassword = !string.IsNullOrEmpty(seedPassword) &&
                    seedPassword.Length >= 8 &&
                    System.Text.Encoding.UTF8.GetByteCount(seedPassword) <= 72 &&
                    seedPassword.Any(char.IsUpper) &&
                    seedPassword.Any(char.IsDigit) &&
                    seedPassword.Any(character => !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character)) &&
                    !seedPassword.Any(char.IsWhiteSpace);
                if (!validSeedPassword)
                    throw new InvalidOperationException("SeedAdmin password must be at least 8 characters, no more than 72 UTF-8 bytes, and contain at least one uppercase letter, one digit, one special character, and no whitespace.");

                var administrator = await Users.Include(user => user.UserRoles).SingleOrDefaultAsync(user => user.Email == seedEmail);
                if (administrator is null)
                {
                    var adminRole = await Roles.SingleAsync(role => role.Name == AppRoles.Admin);
                    Users.Add(new User
                    {
                        Email = seedEmail!,
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword(seedPassword, workFactor: 11),
                        HasLocalProvider = true,
                        IsActive = true,
                        VerifiedAt = DateTime.UtcNow,
                        UserRoles = [new UserRole { Role = adminRole }]
                    });
                }
                // An existing user is never silently promoted or has their password overwritten by seed.
            }
            await SaveChangesAsync();
            await transaction.CommitAsync();
        }
    }
}
