using System;
using System.Threading.Tasks;
using MyLife.Shared.Entities;
using Microsoft.EntityFrameworkCore;

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

            // Seed Generations (Đời 1 -> Đời 5)
            if (!await Generations.AnyAsync())
            {
                var defaultGenerations = new[]
                {
                    new Generation { Id = 1, Name = "Đời 1", Title = "Thế hệ thứ nhất", Description = "Thế hệ khởi thủy / Tiền bối" },
                    new Generation { Id = 2, Name = "Đời 2", Title = "Thế hệ thứ hai", Description = "Thế hệ con thứ nhất" },
                    new Generation { Id = 3, Name = "Đời 3", Title = "Thế hệ thứ ba", Description = "Thế hệ con cháu kế cận" },
                    new Generation { Id = 4, Name = "Đời 4", Title = "Thế hệ thứ tư", Description = "Thế hệ chắt" },
                    new Generation { Id = 5, Name = "Đời 5", Title = "Thế hệ thứ năm", Description = "Thế hệ chút" },
                };
                await Generations.AddRangeAsync(defaultGenerations);
                await SaveChangesAsync();
            }

            // Seed Users: Chỉ 1 tài khoản Admin duy nhất
            if (!await Users.AnyAsync())
            {
                var adminUser = new User
                {
                    Id = 1,
                    Email = "admin@gmail.com",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                    IsActive = true,
                    VerifiedAt = DateTime.UtcNow,
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
