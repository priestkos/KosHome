using Xunit;

namespace KosHome.Host.Tests;

public class MotionTests
{
    private static Spring Run(float fps, float seconds, float target = 1.08f)
    {
        var s = new Spring(1);
        var frames = (int)MathF.Round(fps * seconds);
        for (var i = 0; i < frames; i++) s.Step(target, Motion.Dt(1 / fps));
        return s;
    }

    [Fact]
    public void SpringSettlesOnItsTargetWithinHalfASecond()
    {
        var s = Run(60, 0.5f);
        Assert.InRange(s.Value, 1.08f - 0.002f, 1.08f + 0.002f);
    }

    [Fact]
    public void SpringOvershootStaysSmall()
    {
        var s = new Spring(1);
        var peak = 1f;
        for (var i = 0; i < 120; i++)
        {
            s.Step(1.08f, 1 / 120f);
            peak = MathF.Max(peak, s.Value);
        }

        Assert.True(peak > 1.08f, "a hovered icon should bounce a little past its size");
        Assert.True(peak < 1.08f + 0.08f * 0.2f, $"overshoot {peak} is more than 20% of the move");
    }

    [Theory]
    [InlineData(30)]
    [InlineData(144)]
    [InlineData(240)]
    public void SpringLooksTheSameAtAnyFrameRate(float fps)
    {
        // Sixths of a second are whole frames at 30, 60, 144 and 240 fps, so every run covers the same time.
        foreach (var t in new[] { 1 / 6f, 2 / 6f, 3 / 6f })
            Assert.InRange(Run(fps, t).Value - Run(60, t).Value, -0.004f, 0.004f);
    }

    [Fact]
    public void SpringStaysStableThroughAHitch()
    {
        var s = new Spring(1);
        s.Step(1.08f, Motion.Dt(5f)); // a five second loading screen arrives as one frame
        Assert.True(float.IsFinite(s.Value) && s.Value is > 0.9f and < 1.2f, s.Value.ToString());
    }

    [Theory]
    [InlineData(float.NaN, 0)]
    [InlineData(float.PositiveInfinity, 0)]
    [InlineData(-1, 0)]
    [InlineData(2, 0.05f)]
    [InlineData(0.016f, 0.016f)]
    public void DeltaIsClamped(float raw, float expected) => Assert.Equal(expected, Motion.Dt(raw), 5);

    [Fact]
    public void ApproachIsFrameRateIndependent()
    {
        float Go(int frames)
        {
            var v = 0f;
            for (var i = 0; i < frames; i++) v = Motion.Approach(v, 1, 1f / frames, 14);
            return v;
        }

        Assert.Equal(Go(30), Go(240), 4);
        Assert.Equal(1 - MathF.Exp(-14), Go(60), 4);
    }

    [Fact]
    public void EasesStartAtZeroAndEndAtOne()
    {
        Assert.Equal(0, Motion.EaseOutCubic(0), 5);
        Assert.Equal(1, Motion.EaseOutCubic(1), 5);
        Assert.Equal(0, Motion.EaseOutBack(0), 5);
        Assert.Equal(1, Motion.EaseOutBack(1), 5);
        Assert.Equal(1, Motion.EaseOutCubic(7), 5); // clamped
        var peak = Enumerable.Range(0, 101).Max(i => Motion.EaseOutBack(i / 100f));
        Assert.InRange(peak, 1.05f, 1.12f);
    }

    [Fact]
    public void StaggerDelaysEachItem()
    {
        Assert.Equal(0, Motion.Stagger(0.1f, 3, 0.05f, 0.4f));
        Assert.Equal(0.5f, Motion.Stagger(0.35f, 3, 0.05f, 0.4f), 4);
        Assert.Equal(1, Motion.Stagger(9, 3, 0.05f, 0.4f));
    }

    [Theory]
    [InlineData(0, 0, 0)]        // the epoch is Eorzea midnight
    [InlineData(175, 1, 0)]      // one Eorzean hour is 175 real seconds
    [InlineData(4200, 0, 0)]     // one Eorzean day is 70 real minutes
    [InlineData(2100, 12, 0)]    // half a day
    [InlineData(35, 0, 12)]      // 175 s / 60 min * 12 min
    public void EorzeaTimeFollowsThe175SecondHour(long unixSeconds, int hour, int minute) =>
        Assert.Equal((hour, minute), Motion.EorzeaTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds)));
}

public class ConfigurationTests
{
    [Fact]
    public void DefaultsAreTheTabletLook()
    {
        var c = new Configuration();
        Assert.True(c.Frosted && c.Wallpaper && c.Animations);
        Assert.Equal(Configuration.DefaultOpacity, c.Opacity);
        Assert.False(c.Sanitize());
    }

    [Theory]
    [InlineData(float.NaN, Configuration.DefaultOpacity)]
    [InlineData(-3, Configuration.MinOpacity)]
    [InlineData(0, Configuration.MinOpacity)]
    [InlineData(7, 1)]
    public void OpacityIsPutBackInRange(float stored, float expected)
    {
        var c = new Configuration { Opacity = stored };
        Assert.True(c.Sanitize());
        Assert.Equal(expected, c.Opacity);
    }

    [Fact]
    public void ResetLookKeepsTheAppSwitches()
    {
        var c = new Configuration { Opacity = 0.3f, Frosted = false, Wallpaper = false, Animations = false, DisabledApps = ["Calc14"] };
        c.ResetLook();
        Assert.True(c.Frosted && c.Wallpaper && c.Animations);
        Assert.Equal(Configuration.DefaultOpacity, c.Opacity);
        Assert.Contains("Calc14", c.DisabledApps);
    }

    [Fact]
    public void OldConfigWithoutTheLookFieldsGetsDefaults()
    {
        var c = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>("""{ "Version": 1, "DisabledApps": ["Quill14"], "Frosted": false }""")!;
        Assert.False(c.Frosted);
        Assert.Equal(Configuration.DefaultOpacity, c.Opacity);
        Assert.True(c.Animations && c.Wallpaper);
        Assert.False(c.Sanitize());
    }
}
