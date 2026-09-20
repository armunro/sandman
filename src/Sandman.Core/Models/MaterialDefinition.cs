using System;

namespace Sandman.Core.Models
{
    public enum StateOfMatter
    {
        Empty,
        Solid,          // Immovable solid (Wall, Iron, Stone, Wood)
        MovableSolid,   // Powder/sand (Sand, Gunpowder, Sawdust, Ash)
        Liquid,         // Flows and disperses (Water, Oil, Acid, Lava)
        Gas,            // Floats upward, disperses (Smoke, Steam, Methane)
        Energy          // Ephemeral energy (Fire, Spark, Plasma)
    }

    public class MaterialDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public string Color { get; set; } = "#FFFFFF";
        public int ColorVariation { get; set; } = 8;
        public StateOfMatter State { get; set; } = StateOfMatter.Solid;

        // Physical properties
        public float Density { get; set; } = 1.0f; // Relative density (Air~0.001, Oil~0.8, Water~1.0, Sand~1.5, Stone~2.5, Iron~7.8)
        public int Dispersion { get; set; } = 1;   // Horizontal flow rate per tick (liquids & gases)
        public float Friction { get; set; } = 0.5f; // Resistance to sliding
        public float Hardness { get; set; } = 1.0f; // Resistance to blast / acid

        // Thermodynamic properties
        public float DefaultTemperature { get; set; } = 20.0f; // in Celsius
        public float ThermalConductivity { get; set; } = 0.1f; // 0.0 to 1.0
        public float SpecificHeat { get; set; } = 1.0f;        // Resistance to temperature changes

        // Phase changes
        public float? MeltingPoint { get; set; }
        public string? MeltTarget { get; set; }

        public float? FreezingPoint { get; set; }
        public string? FreezeTarget { get; set; }

        public float? BoilingPoint { get; set; }
        public string? BoilTarget { get; set; }

        public float? CondensationPoint { get; set; }
        public string? CondenseTarget { get; set; }

        // Combustion properties
        public bool IsFlammable { get; set; } = false;
        public float IgnitionTemperature { get; set; } = 300.0f; // in Celsius
        public int Fuel { get; set; } = 100;                     // Burn steps
        public float HeatGenerated { get; set; } = 50.0f;        // Heat added per tick while burning
        public string? BurnProduct { get; set; } = "ash";
        public float SmokeChance { get; set; } = 0.15f;
        public float FireChance { get; set; } = 0.10f;
        public bool IsInstantFuse { get; set; } = false;
        public bool IsWaterproof { get; set; } = false;

        // Explosion properties
        public bool IsExplosive { get; set; } = false;
        public int ExplosionRadius { get; set; } = 12;
        public float ExplosionForce { get; set; } = 10.0f;
        public float ExplosionTemperature { get; set; } = 1200.0f;
        public int ExplosionFireCount { get; set; } = 20;
        public bool SensitiveToShock { get; set; } = false;

        // Chemical & reaction properties
        public float AcidResistance { get; set; } = 0.0f; // 0 = corrodes instantly, 1 = immune
        public bool ExtinguishesFire { get; set; } = false;
        public string? ReactsWith { get; set; }
        public string? ReactionProduct { get; set; }
        public bool ExplodesOnReaction { get; set; } = false;

        // Lifetime
        public int Lifetime { get; set; } = 0; // 0 = permanent; >0 = decays after N ticks
        public string? DecayTarget { get; set; }

        // Special behavior flags
        public bool IsEmitter { get; set; } = false;
        public string? EmitsMaterial { get; set; }
        public bool IsDrain { get; set; } = false;
        public bool FixedTemperature { get; set; } = false;
    }
}
