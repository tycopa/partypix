using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PartyPix.Web.Data.Entities;

namespace PartyPix.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser>(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Media> Media => Set<Media>();
    public DbSet<GuestSession> GuestSessions => Set<GuestSession>();
    public DbSet<GuestbookEntry> GuestbookEntries => Set<GuestbookEntry>();
    public DbSet<Reaction> Reactions => Set<Reaction>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Event>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.Property(x => x.Slug).HasMaxLength(64).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.WelcomeMessage).HasMaxLength(2000);
            e.Property(x => x.Theme).HasMaxLength(64);
            e.Property(x => x.AccessCode).HasMaxLength(64);

            e.HasOne(x => x.Host)
             .WithMany(u => u.Events)
             .HasForeignKey(x => x.HostUserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Album>(a =>
        {
            a.Property(x => x.Name).HasMaxLength(120).IsRequired();
            a.HasOne(x => x.Event).WithMany(e => e.Albums)
             .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Media>(m =>
        {
            m.HasIndex(x => new { x.EventId, x.CreatedAt });
            m.HasIndex(x => new { x.EventId, x.ContentHash });
            m.Property(x => x.OriginalFileName).HasMaxLength(260).IsRequired();
            m.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            m.Property(x => x.StorageKey).HasMaxLength(400).IsRequired();
            m.Property(x => x.ThumbnailKey).HasMaxLength(400);
            m.Property(x => x.DisplayKey).HasMaxLength(400);
            m.Property(x => x.PosterKey).HasMaxLength(400);
            m.Property(x => x.ContentHash).HasMaxLength(64);

            // Event is the single cascade root for Media. Album/GuestSession
            // use ClientSetNull (DB NoAction) to avoid SQL Server "multiple
            // cascade paths" errors — Event → Albums/GuestSessions already
            // cascades to Media directly via FK_Media_Events_EventId.
            m.HasOne(x => x.Event).WithMany(e => e.Media)
             .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            m.HasOne(x => x.Album).WithMany(a => a!.Media)
             .HasForeignKey(x => x.AlbumId).OnDelete(DeleteBehavior.ClientSetNull);
            m.HasOne(x => x.GuestSession).WithMany()
             .HasForeignKey(x => x.GuestSessionId).OnDelete(DeleteBehavior.ClientSetNull);
        });

        b.Entity<GuestSession>(g =>
        {
            g.HasIndex(x => new { x.EventId, x.CookieToken }).IsUnique();
            g.Property(x => x.CookieToken).HasMaxLength(64).IsRequired();
            g.Property(x => x.DisplayName).HasMaxLength(80).IsRequired();
            g.Property(x => x.IpHash).HasMaxLength(128);
            g.Property(x => x.UserAgent).HasMaxLength(400);

            g.HasOne(x => x.Event).WithMany(e => e.GuestSessions)
             .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<GuestbookEntry>(e =>
        {
            e.Property(x => x.Message).HasMaxLength(4000);
            e.HasOne(x => x.Event).WithMany(ev => ev.GuestbookEntries)
             .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Reaction>(r =>
        {
            r.HasIndex(x => new { x.MediaId, x.GuestSessionId, x.Kind });
            r.Property(x => x.Body).HasMaxLength(2000);
            // Media cascade is the single DB path. GuestSession uses
            // ClientCascade (DB NoAction) to avoid "multiple cascade paths":
            // Event → Media → Reactions and Event → GuestSessions → Reactions
            // both terminate here, so only one can be a DB cascade.
            r.HasOne(x => x.Media).WithMany()
             .HasForeignKey(x => x.MediaId).OnDelete(DeleteBehavior.Cascade);
            r.HasOne(x => x.GuestSession).WithMany()
             .HasForeignKey(x => x.GuestSessionId).OnDelete(DeleteBehavior.ClientCascade);
        });
    }
}
