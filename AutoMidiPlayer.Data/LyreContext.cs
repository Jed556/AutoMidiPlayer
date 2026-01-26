using AutoMidiPlayer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoMidiPlayer.Data;

public class LyreContext : DbContext
{
    public LyreContext(DbContextOptions<LyreContext> options) : base(options) { }

    public DbSet<History> History { get; set; } = null!;
}
