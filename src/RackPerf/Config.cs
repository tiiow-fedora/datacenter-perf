using System.Text.Json;

namespace RackPerf;

internal class Config
{
    /// <summary>Master switch. False leaves the game completely untouched.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum seconds between full network route evaluations.
    ///
    /// MEASURED: the game's stock value is 0 - there is no throttle at all, so
    /// every cable edit triggers a complete traversal of every cable against
    /// every device. Editing five cables in ten seconds means five full
    /// re-routes. This is what a COALESCING window fixes: a burst of edits
    /// collapses into one evaluation once you stop changing things.
    ///
    /// Deliberately small. This is not meant to make evaluation rare - it is
    /// meant to stop it running N times for one logical change. The cost is at
    /// most this many seconds before a newly connected cable carries packets.
    /// Idle is unaffected: with a settled network, zero evaluations fire.
    /// </summary>
    public float EvaluationCooldownSeconds { get; set; } = 1.0f;

    /// <summary>
    /// Legacy multiplier on the stock cooldown. INERT in practice: stock is 0,
    /// so any multiple of it is still 0. Kept only so an existing config file
    /// does not fail to parse. Use EvaluationCooldownSeconds.
    /// </summary>
    public float CooldownMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// Per-frame millisecond budget for the evaluation coroutine. 0 = leave the
    /// game's value (20) alone. Lowering this spreads the same work over more
    /// frames; it does not reduce total allocation.
    /// </summary>
    public int EvaluationMsBudget { get; set; } = 0;

    /// <summary>F9 toggles the throttle off/on live, for A/B comparison.</summary>
    public bool EnableHotkey { get; set; } = true;

    public static Config LoadOrCreate(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "config.json");
            if (File.Exists(path))
            {
                var cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(path));
                if (cfg != null) return cfg;
            }

            var fresh = new Config();
            File.WriteAllText(path, JsonSerializer.Serialize(fresh,
                new JsonSerializerOptions { WriteIndented = true }));
            return fresh;
        }
        catch
        {
            return new Config();
        }
    }
}
