using AutoMidiPlayer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoMidiPlayer.Data;

public class PlayerContext(DbContextOptions<PlayerContext> options) : DbContext(options)
{
    public DbSet<Song> Songs { get; set; } = null!;
}
