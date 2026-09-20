using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using Sandman.Core.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Sandman.Core.Config
{
    public class MaterialConfigFile
    {
        public List<MaterialDefinition> Materials { get; set; } = new();
    }

    public class MaterialRuntime
    {
        public ushort Index { get; set; }
        public MaterialDefinition Definition { get; set; } = new();

        public uint BaseColorRgba { get; set; }
        public byte BaseR { get; set; }
        public byte BaseG { get; set; }
        public byte BaseB { get; set; }
        public byte BaseA { get; set; }

        public ushort MeltTargetIndex { get; set; }
        public ushort FreezeTargetIndex { get; set; }
        public ushort BoilTargetIndex { get; set; }
        public ushort CondenseTargetIndex { get; set; }
        public ushort BurnProductIndex { get; set; }
        public ushort DecayTargetIndex { get; set; }
        public ushort EmitsMaterialIndex { get; set; }
        public ushort ReactsWithIndex { get; set; }
        public ushort ReactionProductIndex { get; set; }

        public uint GenerateColor(Random random)
        {
            if (Definition.ColorVariation <= 0)
                return BaseColorRgba;

            int var = Definition.ColorVariation;
            int delta = random.Next(-var, var + 1);

            int r = Math.Clamp(BaseR + delta, 0, 255);
            int g = Math.Clamp(BaseG + delta, 0, 255);
            int b = Math.Clamp(BaseB + delta, 0, 255);

            return (uint)((BaseA << 24) | (r << 16) | (g << 8) | b);
        }
    }

    public class MaterialRegistry
    {
        public const ushort EmptyIndex = 0;
        public static readonly MaterialRuntime EmptyMaterial = new()
        {
            Index = EmptyIndex,
            Definition = new MaterialDefinition
            {
                Id = "air",
                Name = "Air",
                Category = "Tools",
                Color = "#000000",
                ColorVariation = 0,
                State = StateOfMatter.Empty,
                Density = 0.001f,
                ThermalConductivity = 0.05f,
                SpecificHeat = 1.0f,
                DefaultTemperature = 20.0f
            },
            BaseColorRgba = 0xFF000000,
            BaseR = 0,
            BaseG = 0,
            BaseB = 0,
            BaseA = 255
        };

        private readonly List<MaterialRuntime> _materials = new();
        private readonly Dictionary<string, ushort> _idToIndex = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<MaterialRuntime> Materials => _materials;
        public int Count => _materials.Count;

        public MaterialRegistry()
        {
            Reset();
        }

        public void Reset()
        {
            _materials.Clear();
            _idToIndex.Clear();

            _materials.Add(EmptyMaterial);
            _idToIndex["air"] = EmptyIndex;
            _idToIndex["empty"] = EmptyIndex;
        }

        public MaterialRuntime GetMaterial(ushort index)
        {
            if (index < _materials.Count)
                return _materials[index];
            return EmptyMaterial;
        }

        public MaterialRuntime? TryGetMaterial(string id)
        {
            if (_idToIndex.TryGetValue(id, out var index))
                return _materials[index];
            return null;
        }

        public ushort GetIndex(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return EmptyIndex;
            if (_idToIndex.TryGetValue(id, out var index))
                return index;
            return EmptyIndex;
        }

        public void LoadFromYaml(string yamlContent)
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();

            MaterialConfigFile? config = null;
            try
            {
                config = deserializer.Deserialize<MaterialConfigFile>(yamlContent);
            }
            catch
            {
                // Try with CamelCase naming convention if Underscored didn't work as expected
                var camelDeserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();
                config = camelDeserializer.Deserialize<MaterialConfigFile>(yamlContent);
            }

            if (config?.Materials == null || config.Materials.Count == 0)
            {
                throw new InvalidOperationException("No materials found in the YAML configuration.");
            }

            Reset();

            foreach (var mat in config.Materials)
            {
                RegisterMaterial(mat);
            }

            ResolveLinks();
        }

        public void LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Material configuration file not found: {filePath}");

            string yaml = File.ReadAllText(filePath);
            LoadFromYaml(yaml);
        }

        public void RegisterMaterial(MaterialDefinition def)
        {
            if (string.IsNullOrWhiteSpace(def.Id))
                throw new ArgumentException("Material ID cannot be null or empty.");

            if (_idToIndex.ContainsKey(def.Id))
            {
                // Update existing
                var existingIndex = _idToIndex[def.Id];
                _materials[existingIndex] = CreateRuntime(def, existingIndex);
                return;
            }

            ushort index = (ushort)_materials.Count;
            var runtime = CreateRuntime(def, index);
            _materials.Add(runtime);
            _idToIndex[def.Id] = index;
        }

        private MaterialRuntime CreateRuntime(MaterialDefinition def, ushort index)
        {
            var (r, g, b, a) = ParseHexColor(def.Color);
            uint rgba = (uint)((a << 24) | (r << 16) | (g << 8) | b);

            return new MaterialRuntime
            {
                Index = index,
                Definition = def,
                BaseColorRgba = rgba,
                BaseR = r,
                BaseG = g,
                BaseB = b,
                BaseA = a
            };
        }

        public void ResolveLinks()
        {
            // Register handy aliases if primary IDs exist
            if (_idToIndex.TryGetValue("binary_explosive_a", out var bexpA))
            {
                if (!_idToIndex.ContainsKey("binary_explosive")) _idToIndex["binary_explosive"] = bexpA;
                if (!_idToIndex.ContainsKey("binary explosive")) _idToIndex["binary explosive"] = bexpA;
                if (!_idToIndex.ContainsKey("bexp")) _idToIndex["bexp"] = bexpA;
                if (!_idToIndex.ContainsKey("bexp_a")) _idToIndex["bexp_a"] = bexpA;
                if (!_idToIndex.ContainsKey("binary_a")) _idToIndex["binary_a"] = bexpA;
            }
            if (_idToIndex.TryGetValue("binary_explosive_b", out var bexpB))
            {
                if (!_idToIndex.ContainsKey("bexp_b")) _idToIndex["bexp_b"] = bexpB;
                if (!_idToIndex.ContainsKey("binary_b")) _idToIndex["binary_b"] = bexpB;
            }

            // Fuse aliases
            if (_idToIndex.TryGetValue("fuse", out var stdFuse))
            {
                if (!_idToIndex.ContainsKey("standard_fuse")) _idToIndex["standard_fuse"] = stdFuse;
                if (!_idToIndex.ContainsKey("standard fuse")) _idToIndex["standard fuse"] = stdFuse;
                if (!_idToIndex.ContainsKey("normal_fuse")) _idToIndex["normal_fuse"] = stdFuse;
                if (!_idToIndex.ContainsKey("normal fuse")) _idToIndex["normal fuse"] = stdFuse;
            }
            if (_idToIndex.TryGetValue("slow_fuse", out var slowFuse))
            {
                if (!_idToIndex.ContainsKey("slow fuse")) _idToIndex["slow fuse"] = slowFuse;
                if (!_idToIndex.ContainsKey("delay_fuse")) _idToIndex["delay_fuse"] = slowFuse;
                if (!_idToIndex.ContainsKey("delay fuse")) _idToIndex["delay fuse"] = slowFuse;
            }
            if (_idToIndex.TryGetValue("fast_fuse", out var fastFuse))
            {
                if (!_idToIndex.ContainsKey("fast fuse")) _idToIndex["fast fuse"] = fastFuse;
                if (!_idToIndex.ContainsKey("quick_fuse")) _idToIndex["quick_fuse"] = fastFuse;
                if (!_idToIndex.ContainsKey("quick fuse")) _idToIndex["quick fuse"] = fastFuse;
                if (!_idToIndex.ContainsKey("quickmatch")) _idToIndex["quickmatch"] = fastFuse;
            }
            if (_idToIndex.TryGetValue("instant_fuse", out var instFuse))
            {
                if (!_idToIndex.ContainsKey("instant fuse")) _idToIndex["instant fuse"] = instFuse;
                if (!_idToIndex.ContainsKey("flash_fuse")) _idToIndex["flash_fuse"] = instFuse;
                if (!_idToIndex.ContainsKey("flash fuse")) _idToIndex["flash fuse"] = instFuse;
                if (!_idToIndex.ContainsKey("det_cord")) _idToIndex["det_cord"] = instFuse;
                if (!_idToIndex.ContainsKey("detcord")) _idToIndex["detcord"] = instFuse;
            }
            if (_idToIndex.TryGetValue("powder_fuse", out var pFuse))
            {
                if (!_idToIndex.ContainsKey("powder fuse")) _idToIndex["powder fuse"] = pFuse;
                if (!_idToIndex.ContainsKey("granular_fuse")) _idToIndex["granular_fuse"] = pFuse;
            }
            if (_idToIndex.TryGetValue("waterproof_fuse", out var wpFuse))
            {
                if (!_idToIndex.ContainsKey("waterproof fuse")) _idToIndex["waterproof fuse"] = wpFuse;
                if (!_idToIndex.ContainsKey("underwater_fuse")) _idToIndex["underwater_fuse"] = wpFuse;
            }

            // Flammable liquids aliases
            if (_idToIndex.TryGetValue("gasoline", out var gasIdx))
            {
                if (!_idToIndex.ContainsKey("petrol")) _idToIndex["petrol"] = gasIdx;
                if (!_idToIndex.ContainsKey("gas")) _idToIndex["gas"] = gasIdx;
            }
            if (_idToIndex.TryGetValue("ethanol", out var ethIdx))
            {
                if (!_idToIndex.ContainsKey("alcohol")) _idToIndex["alcohol"] = ethIdx;
                if (!_idToIndex.ContainsKey("bioethanol")) _idToIndex["bioethanol"] = ethIdx;
            }
            if (_idToIndex.TryGetValue("kerosene", out var keroIdx))
            {
                if (!_idToIndex.ContainsKey("jet_fuel")) _idToIndex["jet_fuel"] = keroIdx;
                if (!_idToIndex.ContainsKey("jet fuel")) _idToIndex["jet fuel"] = keroIdx;
                if (!_idToIndex.ContainsKey("paraffin")) _idToIndex["paraffin"] = keroIdx;
            }
            if (_idToIndex.TryGetValue("napalm", out var napalmIdx))
            {
                if (!_idToIndex.ContainsKey("gel_fuel")) _idToIndex["gel_fuel"] = napalmIdx;
                if (!_idToIndex.ContainsKey("gel fuel")) _idToIndex["gel fuel"] = napalmIdx;
            }
            if (_idToIndex.TryGetValue("pyrophoric_liquid", out var pyroIdx))
            {
                if (!_idToIndex.ContainsKey("pyrophoric")) _idToIndex["pyrophoric"] = pyroIdx;
                if (!_idToIndex.ContainsKey("triethylaluminium")) _idToIndex["triethylaluminium"] = pyroIdx;
                if (!_idToIndex.ContainsKey("phos_liquid")) _idToIndex["phos_liquid"] = pyroIdx;
            }
            if (_idToIndex.TryGetValue("lamp_oil", out var lampIdx))
            {
                if (!_idToIndex.ContainsKey("lamp oil")) _idToIndex["lamp oil"] = lampIdx;
                if (!_idToIndex.ContainsKey("mineral_oil")) _idToIndex["mineral_oil"] = lampIdx;
                if (!_idToIndex.ContainsKey("mineral oil")) _idToIndex["mineral oil"] = lampIdx;
            }

            // Exotic explosives aliases
            if (_idToIndex.TryGetValue("octanitrocubane", out var oncIdx))
            {
                if (!_idToIndex.ContainsKey("onc")) _idToIndex["onc"] = oncIdx;
                if (!_idToIndex.ContainsKey("cubane")) _idToIndex["cubane"] = oncIdx;
                if (!_idToIndex.ContainsKey("octanitro")) _idToIndex["octanitro"] = oncIdx;
            }
            if (_idToIndex.TryGetValue("azidoazide_azide", out var azideIdx))
            {
                if (!_idToIndex.ContainsKey("azidoazide")) _idToIndex["azidoazide"] = azideIdx;
                if (!_idToIndex.ContainsKey("c2n14")) _idToIndex["c2n14"] = azideIdx;
                if (!_idToIndex.ContainsKey("azide")) _idToIndex["azide"] = azideIdx;
            }
            if (_idToIndex.TryGetValue("thermobaric", out var thermoIdx))
            {
                if (!_idToIndex.ContainsKey("fae")) _idToIndex["fae"] = thermoIdx;
                if (!_idToIndex.ContainsKey("fuel_air_explosive")) _idToIndex["fuel_air_explosive"] = thermoIdx;
                if (!_idToIndex.ContainsKey("fuel air explosive")) _idToIndex["fuel air explosive"] = thermoIdx;
                if (!_idToIndex.ContainsKey("vacuum_bomb")) _idToIndex["vacuum_bomb"] = thermoIdx;
            }
            if (_idToIndex.TryGetValue("mercury_fulminate", out var fulmIdx))
            {
                if (!_idToIndex.ContainsKey("fulminate_of_mercury")) _idToIndex["fulminate_of_mercury"] = fulmIdx;
                if (!_idToIndex.ContainsKey("fulminate")) _idToIndex["fulminate"] = fulmIdx;
                if (!_idToIndex.ContainsKey("mercury fulminate")) _idToIndex["mercury fulminate"] = fulmIdx;
            }
            if (_idToIndex.TryGetValue("dark_matter_bomb", out var dmIdx))
            {
                if (!_idToIndex.ContainsKey("dark_matter")) _idToIndex["dark_matter"] = dmIdx;
                if (!_idToIndex.ContainsKey("dark matter")) _idToIndex["dark matter"] = dmIdx;
                if (!_idToIndex.ContainsKey("singularity_bomb")) _idToIndex["singularity_bomb"] = dmIdx;
                if (!_idToIndex.ContainsKey("singularity")) _idToIndex["singularity"] = dmIdx;
            }
            if (_idToIndex.TryGetValue("plasma_bomb", out var plasmaIdx))
            {
                if (!_idToIndex.ContainsKey("plasma_charge")) _idToIndex["plasma_charge"] = plasmaIdx;
                if (!_idToIndex.ContainsKey("plasma charge")) _idToIndex["plasma charge"] = plasmaIdx;
                if (!_idToIndex.ContainsKey("plasma_core")) _idToIndex["plasma_core"] = plasmaIdx;
                if (!_idToIndex.ContainsKey("plasma core")) _idToIndex["plasma core"] = plasmaIdx;
            }
            if (_idToIndex.TryGetValue("antimatter", out var antiIdx))
            {
                if (!_idToIndex.ContainsKey("antimatter_bomb")) _idToIndex["antimatter_bomb"] = antiIdx;
                if (!_idToIndex.ContainsKey("antimatter bomb")) _idToIndex["antimatter bomb"] = antiIdx;
                if (!_idToIndex.ContainsKey("micro_nuke")) _idToIndex["micro_nuke"] = antiIdx;
                if (!_idToIndex.ContainsKey("micronuke")) _idToIndex["micronuke"] = antiIdx;
                if (!_idToIndex.ContainsKey("nuke")) _idToIndex["nuke"] = antiIdx;
            }
            if (_idToIndex.TryGetValue("chrono_charge", out var chronoIdx))
            {
                if (!_idToIndex.ContainsKey("aetherite")) _idToIndex["aetherite"] = chronoIdx;
                if (!_idToIndex.ContainsKey("chrono charge")) _idToIndex["chrono charge"] = chronoIdx;
                if (!_idToIndex.ContainsKey("temporal_charge")) _idToIndex["temporal_charge"] = chronoIdx;
                if (!_idToIndex.ContainsKey("time_bomb")) _idToIndex["time_bomb"] = chronoIdx;
            }

            foreach (var mat in _materials)
            {
                if (mat.Index == EmptyIndex) continue;

                mat.MeltTargetIndex = GetIndex(mat.Definition.MeltTarget);
                mat.FreezeTargetIndex = GetIndex(mat.Definition.FreezeTarget);
                mat.BoilTargetIndex = GetIndex(mat.Definition.BoilTarget);
                mat.CondenseTargetIndex = GetIndex(mat.Definition.CondenseTarget);
                mat.BurnProductIndex = GetIndex(mat.Definition.BurnProduct);
                mat.DecayTargetIndex = GetIndex(mat.Definition.DecayTarget);
                mat.EmitsMaterialIndex = GetIndex(mat.Definition.EmitsMaterial);
                mat.ReactsWithIndex = GetIndex(mat.Definition.ReactsWith);
                mat.ReactionProductIndex = GetIndex(mat.Definition.ReactionProduct);
            }
        }

        public static (byte R, byte G, byte B, byte A) ParseHexColor(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return (255, 255, 255, 255);

            hex = hex.Trim().TrimStart('#');
            if (hex.Length == 6 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                byte r = (byte)((rgb >> 16) & 0xFF);
                byte g = (byte)((rgb >> 8) & 0xFF);
                byte b = (byte)(rgb & 0xFF);
                return (r, g, b, 255);
            }
            if (hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba))
            {
                byte r = (byte)((rgba >> 24) & 0xFF);
                byte g = (byte)((rgba >> 16) & 0xFF);
                byte b = (byte)((rgba >> 8) & 0xFF);
                byte a = (byte)(rgba & 0xFF);
                return (r, g, b, a);
            }
            if (hex.Length == 3 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb3))
            {
                byte r = (byte)(((rgb3 >> 8) & 0xF) * 17);
                byte g = (byte)(((rgb3 >> 4) & 0xF) * 17);
                byte b = (byte)((rgb3 & 0xF) * 17);
                return (r, g, b, 255);
            }

            return (200, 200, 200, 255);
        }

        public static MaterialRegistry CreateDefault()
        {
            var registry = new MaterialRegistry();
            registry.LoadFromYaml(DefaultMaterialsYaml.Content);
            return registry;
        }
    }
}
