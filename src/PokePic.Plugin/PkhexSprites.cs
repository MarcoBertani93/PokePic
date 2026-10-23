using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using PKHeX.Core;
using PokePic.Adapter;

namespace PokePic.Plugin;

/// <summary>
/// The sprites PKHeX shows, from its PKHeX.Drawing.PokeSprite (loaded by PKHeX, not published as a package: found
/// by reflection). This is the only place where System.Drawing is used: the bitmaps become PNG bytes here.
/// </summary>
internal sealed class PkhexSprites : ISpriteSource
{
    private readonly MethodInfo? _species;
    private readonly MethodInfo? _ball;
    private readonly MethodInfo? _item;

    public PkhexSprites()
    {
        var util = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name == "PKHeX.Drawing.PokeSprite")
            .Select(a => a.GetType("PKHeX.Drawing.PokeSprite.SpriteUtil"))
            .FirstOrDefault(t => t is not null);
        if (util is null)
            return;
        var methods = util.GetMethods(BindingFlags.Public | BindingFlags.Static);
        _species = methods.FirstOrDefault(m => m.Name == "GetSprite" && m.GetParameters() is { Length: 8 } p && p[0].ParameterType == typeof(ushort));
        _ball = methods.FirstOrDefault(m => m.Name == "GetBallSprite" && m.GetParameters().Length == 1);
        _item = methods.FirstOrDefault(m => m.Name == "GetItemSprite" && m.GetParameters().Length == 1);
    }

    /// <summary>False outside PKHeX (e.g. in tests): the cards then get transparent images.</summary>
    public bool Available => _species is not null;

    /// <summary>The species sprite without the held-item mark (the item has its own image).</summary>
    public byte[]? Species(PKM pkm)
    {
        uint formArgument = pkm is IFormArgument f ? f.FormArgument : 0;
        return Png(_species?.Invoke(null, [pkm.Species, pkm.Form, pkm.Gender, formArgument, 0, pkm.IsEgg, pkm.IsShiny ? Shiny.Always : Shiny.Never, pkm.Context]));
    }

    public byte[]? Ball(PKM pkm) => Png(_ball?.Invoke(null, [pkm.Ball]));

    public byte[]? HeldItem(PKM pkm) => Png(_item?.Invoke(null, [(int)pkm.HeldItem]));

    private static byte[]? Png(object? image)
    {
        if (image is not Image bitmap)
            return null;
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
