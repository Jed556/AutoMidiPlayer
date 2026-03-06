using AutoMidiPlayer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoMidiPlayer.Data;

public class LyreContext(DbContextOptions<LyreContext> options) : DbContext(options)
{
    public DbSet<Song> Songs { get; set; } = null!;
}
