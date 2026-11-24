using AltusIQ.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AltusIQ.Api.Data;

public class AltusIqDbContext(DbContextOptions<AltusIqDbContext> options) : DbContext(options)
{
    public DbSet<Flight> Flights => Set<Flight>();
    public DbSet<AnomalyEvent> AnomalyEvents => Set<AnomalyEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.Entity<Flight>(builder =>
        {
            builder.HasKey(f => f.Id);

            builder.Property(f => f.Id)
                .HasDefaultValueSql("gen_random_uuid()");

            builder.Property(f => f.Icao24)
                .HasMaxLength(6)
                .IsRequired();

            builder.Property(f => f.Callsign)
                .HasMaxLength(8);

            builder.Property(f => f.OriginCountry)
                .HasMaxLength(64);

            builder.Property(f => f.OpenedAt)
                .HasColumnType("timestamp with time zone");

            builder.Property(f => f.ClosedAt)
                .HasColumnType("timestamp with time zone");

            builder.Property(f => f.LastPosition)
                .HasColumnType("geometry(Point, 4326)");

            builder.Property(f => f.TrackPoints)
                .HasColumnType("jsonb");

            builder.Property(f => f.DepartureAirport)
                .HasMaxLength(4);

            builder.Property(f => f.ArrivalAirport)
                .HasMaxLength(4);

            builder.Property(f => f.EnrichedAt)
                .HasColumnType("timestamp with time zone");

            builder.Property(f => f.EnrichmentAttempts)
                .HasDefaultValue(0);

            builder.HasIndex(f => f.Icao24);
            builder.HasIndex(f => f.OpenedAt);
            builder.HasIndex(f => f.ClosedAt);
            builder.HasIndex(f => f.EnrichedAt);

            builder.HasIndex(f => f.LastPosition)
                .HasMethod("gist");
        });

        modelBuilder.Entity<AnomalyEvent>(builder =>
        {
            builder.HasKey(e => e.Id);

            // No default: the detector derives the id from icao24 + confirm time
            // so a retried write hits the same row. Never FK'd to Flights, since
            // a global event usually has no Flights row at all.
            builder.Property(e => e.Icao24).HasMaxLength(6).IsRequired();
            builder.Property(e => e.Callsign).HasMaxLength(8);
            builder.Property(e => e.OriginCountry).HasMaxLength(64);
            builder.Property(e => e.Squawk).HasMaxLength(4).IsRequired();
            builder.Property(e => e.Kind).HasMaxLength(32).IsRequired();
            builder.Property(e => e.Status).HasMaxLength(16).IsRequired();

            builder.Property(e => e.FirstSeenAt).HasColumnType("timestamp with time zone");
            builder.Property(e => e.ConfirmedAt).HasColumnType("timestamp with time zone");
            builder.Property(e => e.LastSeenAt).HasColumnType("timestamp with time zone");
            builder.Property(e => e.ClosedAt).HasColumnType("timestamp with time zone");
            builder.Property(e => e.CreatedAt).HasColumnType("timestamp with time zone");

            builder.Property(e => e.EvidenceJson).HasColumnType("jsonb");
            builder.Property(e => e.InvestigationJson).HasColumnType("jsonb");
            builder.Property(e => e.InvestigationTraceJson).HasColumnType("jsonb");

            builder.Property(e => e.InvestigationStatus).HasMaxLength(24).IsRequired();
            builder.Property(e => e.PromptVersion).HasMaxLength(32);
            builder.Property(e => e.Provider).HasMaxLength(32);
            builder.Property(e => e.Model).HasMaxLength(96);

            builder.HasIndex(e => e.ConfirmedAt);
            builder.HasIndex(e => e.Icao24);
            builder.HasIndex(e => e.Status);
        });
    }
}
