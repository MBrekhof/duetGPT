using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace duetGPT.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Document> Documents { get; set; }
        public DbSet<ApplicationUser> ApplicationUsers { get; set; }
        public DbSet<DuetThread> Threads { get; set; }
        public DbSet<DuetMessage> Messages { get; set; }
        public DbSet<ThreadDocument> ThreadDocuments { get; set; }
        public DbSet<Prompt> Prompts { get; set; }
        public DbSet<Knowledge> Knowledge { get; set; }
        public DbSet<KnowledgeResult> KnowledgeResults { get; set; }
        public DbSet<KnowledgeQueryResult> KnowledgeQueryResults { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configure DuetMessage entity
            builder.Entity<DuetMessage>(entity =>
            {
                entity.ToTable("DuetMessage");
                entity.HasOne(d => d.Thread)
                    .WithMany(p => p.Messages)
                    .HasForeignKey(d => d.ThreadId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Add indexes for commonly queried fields
                entity.HasIndex(e => e.Created);
                entity.HasIndex(e => e.ThreadId);
            });

            // Configure DuetThread entity
            builder.Entity<DuetThread>(entity =>
            {
                entity.ToTable("Threads");
                entity.HasMany(d => d.Messages)
                    .WithOne(p => p.Thread)
                    .HasForeignKey(d => d.ThreadId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Add index for UserId since we filter by it
                entity.HasIndex(e => e.UserId);
            });

            // Configure KnowledgeResult and KnowledgeQueryResult as non-persisted entities
            builder.Entity<KnowledgeResult>().HasNoKey();
            builder.Entity<KnowledgeQueryResult>().HasNoKey();

            builder.HasPostgresExtension("vector");
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.EnableSensitiveDataLogging(); // Add this to help with debugging
            optionsBuilder.UseNpgsql(o => o.UseVector());
        }
    }
}
