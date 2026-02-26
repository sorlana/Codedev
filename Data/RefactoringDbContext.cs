using Microsoft.EntityFrameworkCore;
using CSharpRefactoringAssistant.Models;

namespace CSharpRefactoringAssistant.Data;

public class RefactoringDbContext : DbContext
{
    public RefactoringDbContext(DbContextOptions<RefactoringDbContext> options)
        : base(options)
    {
    }

    public DbSet<Dialogue> Dialogues { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<Checkpoint> Checkpoints { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Message relationship
        modelBuilder.Entity<Message>()
            .HasOne(m => m.Dialogue)
            .WithMany(d => d.Messages)
            .HasForeignKey(m => m.DialogueId)
            .OnDelete(DeleteBehavior.Cascade);

        // Configure Checkpoint relationship
        modelBuilder.Entity<Checkpoint>()
            .HasOne(c => c.Dialogue)
            .WithMany(d => d.Checkpoints)
            .HasForeignKey(c => c.DialogueId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
