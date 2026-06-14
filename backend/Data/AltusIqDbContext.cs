using AltusIQ.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AltusIQ.Api.Data;

public class AltusIqDbContext(DbContextOptions<AltusIqDbContext> options) : DbContext(options)
{
    public DbSet<Flight> Flights => Set<Flight>();

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

            builder.HasIndex(f => f.Icao24);
            builder.HasIndex(f => f.OpenedAt);
            builder.HasIndex(f => f.ClosedAt);

            builder.HasIndex(f => f.LastPosition)
                .HasMethod("gist");
        });
    }
}