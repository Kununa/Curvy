namespace Curvy.Core.AutoEq;

public sealed record AutoEqHeadphone(string Name, IReadOnlyList<AutoEqIndexEntry> Results);

public static class AutoEqSearch
{
    public static IReadOnlyList<AutoEqHeadphone> Search(IReadOnlyList<AutoEqIndexEntry> index, string query, int max = 50)
    {
        if (string.IsNullOrWhiteSpace(query) || max <= 0) return [];
        var q = query.Trim();
        var tokens = q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // name -> (rank, entries)
        var groups = new Dictionary<string, (int Rank, List<AutoEqIndexEntry> Items)>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in index)
        {
            var name = e.Name;
            if (groups.TryGetValue(name, out var g)) { g.Items.Add(e); continue; }
            int rank;
            if (name.Equals(q, StringComparison.OrdinalIgnoreCase)) rank = 0;
            else if (name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) rank = 1;
            else
            {
                var all = true;
                foreach (var t in tokens)
                    if (name.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0) { all = false; break; }
                if (!all) continue;
                rank = 2;
            }
            groups[name] = (rank, [e]);
        }

        return groups
            .OrderBy(kv => kv.Value.Rank)
            .ThenBy(kv => kv.Key.Length)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Take(max)
            .Select(kv => new AutoEqHeadphone(kv.Value.Items[0].Name,
                kv.Value.Items.OrderBy(SourceRank)
                    .ThenBy(x => x.Source, StringComparer.Ordinal)
                    .ThenBy(x => x.Rig, StringComparer.Ordinal).ToList()))
            .ToList();
    }

    private static int SourceRank(AutoEqIndexEntry e) =>
        e.Source == "oratory1990" ? 0 : e.Source == "crinacle" ? 1 : 2;
}
