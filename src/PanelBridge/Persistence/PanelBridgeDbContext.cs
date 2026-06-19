using Microsoft.EntityFrameworkCore;
using PanelBridge.Domain;

namespace PanelBridge.Persistence;

public class PanelBridgeDbContext(DbContextOptions<PanelBridgeDbContext> options) : DbContext(options)
{
    public DbSet<CaseLookup> CaseLookups => Set<CaseLookup>();
    public DbSet<Panel> Panels => Set<Panel>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<CaseType> CaseTypes => Set<CaseType>();
    public DbSet<Lender> Lenders => Set<Lender>();
    public DbSet<Milestone> Milestones => Set<Milestone>();
    public DbSet<CaseHandler> CaseHandlers => Set<CaseHandler>();
    public DbSet<CaseHandlerPanel> CaseHandlerPanels => Set<CaseHandlerPanel>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<CaseLookup>(e =>
        {
            e.HasKey(x => x.UniversalId);
            e.Property(x => x.PanelRef).HasMaxLength(64);
            e.Property(x => x.InternalRef).HasMaxLength(64);
            e.HasIndex(x => new { x.PanelId, x.PanelRef }).IsUnique();
            e.HasIndex(x => x.InternalRef);
        });

        b.Entity<Panel>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Region>().HasIndex(x => x.Name).IsUnique();
        b.Entity<CaseType>().HasIndex(x => x.Name).IsUnique();

        b.Entity<Lender>(e =>
        {
            e.HasIndex(x => x.SortReferId).IsUnique().HasFilter("[SortReferId] IS NOT NULL");
            e.HasIndex(x => x.EconId).IsUnique().HasFilter("[EconId] IS NOT NULL");
        });

        b.Entity<Milestone>(e =>
        {
            e.HasIndex(x => new { x.PanelId, x.CaseTypeId, x.RegionId, x.PanelMilestoneCode }).IsUnique();
            e.Property(x => x.PanelMilestoneCode).HasMaxLength(64);
        });

        b.Entity<CaseHandler>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<CaseHandlerPanel>(e =>
        {
            e.HasIndex(x => new { x.CaseHandlerId, x.PanelId }).IsUnique();
        });

        b.Entity<Panel>().HasData(
            new Panel { Id = 1, Name = PanelKey.SortRefer },
            new Panel { Id = 2, Name = PanelKey.Econ });
    }
}
