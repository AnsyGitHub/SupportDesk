using Microsoft.EntityFrameworkCore;
using SupportDesk.Core.Tickets;

namespace SupportDesk.Core.Data;

public class SupportDeskDbContext(DbContextOptions<SupportDeskDbContext> options) : DbContext(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var ticket = modelBuilder.Entity<Ticket>();
        ticket.Property(ticket => ticket.Title).HasMaxLength(200).IsRequired();
        ticket.Property(ticket => ticket.Description).HasMaxLength(5000).IsRequired();
        ticket.Property(ticket => ticket.Status).HasConversion<string>();
        ticket.Property(ticket => ticket.Priority).HasConversion<string>();
        ticket.Property(ticket => ticket.Category).HasConversion<string>();
        ticket.Property(ticket => ticket.Sentiment).HasConversion<string>();
        // SQLite does not preserve DateTime.Kind when reading UTC timestamps.
        ticket.Property(ticket => ticket.CreatedAt).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        ticket.Property(ticket => ticket.UpdatedAt).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
