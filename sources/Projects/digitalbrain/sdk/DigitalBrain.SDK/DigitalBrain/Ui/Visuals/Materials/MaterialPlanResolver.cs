namespace DigitalBrain.SDK.DigitalBrain.Ui.Visuals.Materials;

public static class MaterialPlanResolver
{
    public static MaterialPlan Derive(
        string surface,
        string tier,
        string themeBrightness,
        MaterialOverride over)
    {
        var baseTone = ToneFor(surface, themeBrightness);
        var (sigma, chroma, specHz, refract, motion) = DefaultsFor(surface, tier);

        return new MaterialPlan(
            Surface: surface,
            Sigma: over.Sigma ?? sigma,
            Chromaticity: over.Chromaticity ?? chroma,
            SpecPhaseHz: over.SpecPhaseHz ?? specHz,
            Refract: over.Refract ?? refract,
            ToneArgb: over.ToneArgb ?? baseTone,
            MotionPreset: over.MotionPreset ?? motion);
    }

    private static (double sigma, double chroma, double specHz, double refract, string motion)
        DefaultsFor(string surface, string tier)
    {
        var clampedTier = tier is "smooth" or "strained" ? tier : "red";

        if (surface.StartsWith("glow.", StringComparison.Ordinal))
        {
            return clampedTier switch
            {
                "smooth"   => (0.0, 0.0, 0.0, 0.0, "full"),
                "strained" => (0.0, 0.0, 0.0, 0.0, "reduced"),
                _          => (0.0, 0.0, 0.0, 0.0, "minimal"),
            };
        }

        return clampedTier switch
        {
            "smooth"   => (22.0, 1.0, 0.20, 1.0, "full"),
            "strained" => (14.0, 0.6, 0.12, 0.5, "reduced"),
            _          => ( 0.0, 0.0, 0.00, 0.0, "minimal"),
        };
    }

    private static uint ToneFor(string surface, string themeBrightness)
    {
        // DigitalBrain-distinct: glow surfaces use a domain-neutral white at 70% alpha —
        // domain-specific tinting is applied at the call site, not here.
        if (surface.StartsWith("glow.", StringComparison.Ordinal))
            return 0xB3FFFFFF; // 70% white

        return themeBrightness == "light"
            ? 0x0A000000u  // 4% black on light — subtler wash
            : 0x1EFFFFFFu; // 12% white on dark — more presence
    }
}
