using System.Text.Json;

namespace RackPerf;

internal class Config
{
    /// <summary>Master switch. False leaves the game completely untouched.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Multiply the game's stock route-evaluation cooldown by this. 1.0 = stock.
    /// Ignored when EvaluationCooldownSeconds is set explicitly.
    /// </summary>
    public float CooldownMultiplier { get; set; } = 3.0f;

    /// <summary>
    /// Absolute cooldown in seconds. 0 = use CooldownMultiplier instead.
    /// Higher means less allocation but a longer delay before a newly connected
    /// cable starts carrying packets.
    /// </summary>
    public float EvaluationCooldownSeconds { get; set; } = 0f;

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
