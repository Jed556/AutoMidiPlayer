using AutoMidiPlayer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoMidiPlayer.Data;

public class LyreContext : DbContext
{
    public LyreContext(DbContextOptions<LyreContext> options) : base(options) { }

    public DbSet<Song> Songs { get; set; } = null!;
}
