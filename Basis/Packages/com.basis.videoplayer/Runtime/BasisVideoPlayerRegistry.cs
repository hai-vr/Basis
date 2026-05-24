using System;
using System.Collections.Generic;

public static class BasisVideoPlayerRegistry
{
    private static readonly List<BasisVideoPlayer> players = new List<BasisVideoPlayer>();

    public static IReadOnlyList<BasisVideoPlayer> Players => players;
    public static int Count => players.Count;

    public static event Action OnChanged;

    public static void Add(BasisVideoPlayer player)
    {
        if (player == null) return;
        if (players.Contains(player)) return;
        players.Add(player);
        OnChanged?.Invoke();
    }

    public static void Remove(BasisVideoPlayer player)
    {
        if (player == null) return;
        if (players.Remove(player)) OnChanged?.Invoke();
    }
}
