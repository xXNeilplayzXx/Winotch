using Winotch;

namespace Winotch.Tests;

/// <summary>The settings half of the shell skin: Glance is a repaint of the
/// classic notch, so choosing it never changes size, position or layout — an
/// unknown persisted value must fall back to Classic instead of leaving every
/// geometry branch comparing against a value nothing matches.</summary>
public class ShellLayoutSettingsTests
{
    [Fact]
    public void Defaults_ToTheClassicPill()
    {
        var appearance = new AppearanceSettings();
        Assert.Equal(ShellLayoutMode.Classic, appearance.ShellLayout);
        Assert.False(appearance.GlossyGlass);
    }

    [Fact]
    public void UnknownPersistedLayout_NormalizesToClassic()
    {
        var appearance = new AppearanceSettings { ShellLayout = (ShellLayoutMode)99 };
        Assert.Equal(ShellLayoutMode.Classic, appearance.Normalize().ShellLayout);
    }

    [Fact]
    public void GlanceLayout_SurvivesNormalization()
    {
        var appearance = new AppearanceSettings
        {
            ShellLayout = ShellLayoutMode.Glance,
            GlossyGlass = true,
        }.Normalize();
        Assert.Equal(ShellLayoutMode.Glance, appearance.ShellLayout);
        Assert.True(appearance.GlossyGlass);
    }

    [Fact]
    public void Glance_IsTheGlassSkinWithoutTheSeparateToggle()
    {
        var glance = new AppearanceSettings { ShellLayout = ShellLayoutMode.Glance };
        Assert.True(glance.UsesGlossySkin);
    }

    [Fact]
    public void Classic_UsesGlassOnlyWhenTheToggleIsOn()
    {
        Assert.False(new AppearanceSettings().UsesGlossySkin);
        Assert.True(new AppearanceSettings { GlossyGlass = true }.UsesGlossySkin);
    }

    [Fact]
    public void SwitchingFromGlanceToClassic_RestoresExplicitGlossyGlassPreference()
    {
        var classicFlat = new AppearanceSettings().WithShellLayout(ShellLayoutMode.Glance)
            .WithShellLayout(ShellLayoutMode.Classic);
        Assert.False(classicFlat.GlossyGlass);
        Assert.False(classicFlat.UsesGlossySkin);

        var classicGlossy = new AppearanceSettings { GlossyGlass = true }
            .WithShellLayout(ShellLayoutMode.Glance)
            .WithShellLayout(ShellLayoutMode.Classic);
        Assert.True(classicGlossy.GlossyGlass);
        Assert.True(classicGlossy.UsesGlossySkin);
    }

    [Fact]
    public void SelectingGlance_LeavesClassicGlossyGlassPreferenceUnchanged()
    {
        var before = new AppearanceSettings();
        var glance = before.WithShellLayout(ShellLayoutMode.Glance);
        Assert.Equal(before.GlossyGlass, glance.GlossyGlass);
        Assert.True(glance.UsesGlossySkin);
    }

    [Fact]
    public void GlanceLayout_KeepsEveryClassicGeometrySetting()
    {
        // The skin is paint-only: the size, edge, open direction and drag
        // offset all survive untouched so the notch keeps its classic shape.
        var appearance = new AppearanceSettings
        {
            ShellLayout = ShellLayoutMode.Glance,
            Width = 420,
            Height = 52,
            SizePreset = "large",
            CustomSize = true,
            OffsetX = 24,
            OffsetY = 8,
        }.Normalize();

        var classic = appearance with { ShellLayout = ShellLayoutMode.Classic };
        Assert.Equal(classic.Width, appearance.Width);
        Assert.Equal(classic.Height, appearance.Height);
        Assert.Equal(classic.SizePreset, appearance.SizePreset);
        Assert.Equal(classic.CustomSize, appearance.CustomSize);
        Assert.Equal(classic.OffsetX, appearance.OffsetX);
        Assert.Equal(classic.OffsetY, appearance.OffsetY);
    }
}
