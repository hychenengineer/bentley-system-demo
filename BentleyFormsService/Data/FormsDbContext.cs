using Microsoft.EntityFrameworkCore;
using BentleyFormsService.Models.Entities;

namespace BentleyFormsService.Data;

public class FormsDbContext : DbContext
{
    public FormsDbContext(DbContextOptions<FormsDbContext> options) : base(options) { }

    public DbSet<FormDefinitionEntity> FormDefinitions => Set<FormDefinitionEntity>();
    public DbSet<FormFieldEntity> FormFields => Set<FormFieldEntity>();
    public DbSet<WorkflowDefinitionEntity> Workflows => Set<WorkflowDefinitionEntity>();
    public DbSet<WorkflowStateEntity> WorkflowStates => Set<WorkflowStateEntity>();
    public DbSet<FormDataEntity> Forms => Set<FormDataEntity>();
    public DbSet<FormAuditLogEntity> FormAuditLogs => Set<FormAuditLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WorkflowDefinitionEntity>()
            .HasKey(w => w.WorkflowType);

        modelBuilder.Entity<FormDefinitionEntity>()
            .HasKey(f => f.Id);

        modelBuilder.Entity<FormDefinitionEntity>()
            .HasMany(f => f.Fields)
            .WithOne()
            .HasForeignKey(field => field.FormDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WorkflowDefinitionEntity>()
            .HasMany(w => w.States)
            .WithOne()
            .HasForeignKey(s => s.WorkflowType)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FormDataEntity>()
            .HasKey(f => f.Id);

        modelBuilder.Entity<FormDataEntity>()
            .HasMany(f => f.AuditLogs)
            .WithOne()
            .HasForeignKey(log => log.FormDataId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
