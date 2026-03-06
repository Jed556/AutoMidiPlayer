#nullable enable
using System;

namespace AutoMidiPlayer.WPF.Helpers;

public class CaptionedObject<T>(T o, string? caption = null)
{
    public T Object { get; } = o;

    protected string? Caption { get; } = caption;

    public override string ToString() => Caption ?? base.ToString() ?? string.Empty;
}

public class CaptionedObject<T, TEnum>(T o, TEnum type, string? caption = null) : CaptionedObject<T>(o, caption) where T : Enum
{
    public TEnum Type { get; } = type;
    public override string ToString() => Caption ?? Type?.ToString() ?? base.ToString();
}
