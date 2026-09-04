using System;
using System.Threading.Tasks;
using LoginApp.Entities;
using Microsoft.EntityFrameworkCore;

namespace LoginApp.Data
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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 1. Table USERS
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("users");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
                entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(255).IsRequired();
                entity.HasIndex(e => e.Email).IsUnique();
                entity.Property(e => e.PasswordHash).HasColumnName("password_hash").HasMaxLength(255).IsRequired();
                entity.Property(e => e.AvatarUrl).HasColumnName("avatar_url").HasColumnType("text");
                entity.Property(e => e.AuthProvider).HasColumnName("auth_provider").HasDefaultValue(0);
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
                entity.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
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
        }

        /// <summary>
        /// Tự động chèn dữ liệu mẫu (Roles & Duy nhất 1 tài khoản Admin) khi database rỗng
        /// </summary>
        public async Task SeedDataAsync()
        {
            // Seed Roles
            if (!await Roles.AnyAsync())
            {
                var adminRole = new Role { Id = 1, Name = "ADMIN", Description = "Quản trị viên hệ thống" };
                var userRole = new Role { Id = 2, Name = "USER", Description = "Người dùng tiêu chuẩn" };
                await Roles.AddRangeAsync(adminRole, userRole);
                await SaveChangesAsync();
            }

            // Seed Users: Chỉ 1 tài khoản Admin duy nhất
            if (!await Users.AnyAsync())
            {
                var adminUser = new User
                {
                    Id = Guid.Parse("a0000000-0000-0000-0000-000000000001"),
                    Email = "admin@gmail.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                    IsActive = true,
                    VerifiedAt = DateTime.UtcNow
                };

                await Users.AddAsync(adminUser);
                await SaveChangesAsync();

                // Assign Role ADMIN
                var adminUserRole = new UserRole { UserId = adminUser.Id, RoleId = 1 };
                await UserRoles.AddAsync(adminUserRole);
                await SaveChangesAsync();
            }
        }
    }
}
