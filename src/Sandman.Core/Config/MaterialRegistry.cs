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

        public bool SuppressHeatGlow { get; set; }
        public bool IsTorch { get; set; }

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
            BaseA = 255,
            SuppressHeatGlow = true
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

            bool suppressGlow = def.State == StateOfMatter.Energy ||
                                def.IsEmitter ||
                                def.FixedTemperature ||
                                string.Equals(def.Category, "Tools", StringComparison.OrdinalIgnoreCase);

            bool isTorch = def.Id.Contains("torch", StringComparison.OrdinalIgnoreCase) ||
                           def.Name.Contains("Torch", StringComparison.OrdinalIgnoreCase);

            return new MaterialRuntime
            {
                Index = index,
                Definition = def,
                BaseColorRgba = rgba,
                BaseR = r,
                BaseG = g,
                BaseB = b,
                BaseA = a,
                SuppressHeatGlow = suppressGlow,
                IsTorch = isTorch
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

            // Water-reactive explosives aliases
            if (_idToIndex.TryGetValue("sodium_powder", out var naPowderIdx))
            {
                if (!_idToIndex.ContainsKey("sodium powder")) _idToIndex["sodium powder"] = naPowderIdx;
                if (!_idToIndex.ContainsKey("sodium_dust")) _idToIndex["sodium_dust"] = naPowderIdx;
                if (!_idToIndex.ContainsKey("sodium dust")) _idToIndex["sodium dust"] = naPowderIdx;
                if (!_idToIndex.ContainsKey("water_reactive_powder")) _idToIndex["water_reactive_powder"] = naPowderIdx;
            }
            if (_idToIndex.TryGetValue("potassium_powder", out var kPowderIdx))
            {
                if (!_idToIndex.ContainsKey("potassium powder")) _idToIndex["potassium powder"] = kPowderIdx;
                if (!_idToIndex.ContainsKey("potassium_dust")) _idToIndex["potassium_dust"] = kPowderIdx;
                if (!_idToIndex.ContainsKey("potassium dust")) _idToIndex["potassium dust"] = kPowderIdx;
            }
            if (_idToIndex.TryGetValue("sodium", out var naIdx))
            {
                if (!_idToIndex.ContainsKey("metallic_sodium")) _idToIndex["metallic_sodium"] = naIdx;
                if (!_idToIndex.ContainsKey("metallic sodium")) _idToIndex["metallic sodium"] = naIdx;
                if (!_idToIndex.ContainsKey("water_reactive_solid")) _idToIndex["water_reactive_solid"] = naIdx;
            }
            if (_idToIndex.TryGetValue("potassium", out var kIdx))
            {
                if (!_idToIndex.ContainsKey("metallic_potassium")) _idToIndex["metallic_potassium"] = kIdx;
                if (!_idToIndex.ContainsKey("metallic potassium")) _idToIndex["metallic potassium"] = kIdx;
            }
            if (_idToIndex.TryGetValue("caesium", out var csIdx))
            {
                if (!_idToIndex.ContainsKey("cesium")) _idToIndex["cesium"] = csIdx;
                if (!_idToIndex.ContainsKey("metallic_caesium")) _idToIndex["metallic_caesium"] = csIdx;
                if (!_idToIndex.ContainsKey("metallic caesium")) _idToIndex["metallic caesium"] = csIdx;
                if (!_idToIndex.ContainsKey("metallic_cesium")) _idToIndex["metallic_cesium"] = csIdx;
                if (!_idToIndex.ContainsKey("metallic cesium")) _idToIndex["metallic cesium"] = csIdx;
            }
            if (_idToIndex.TryGetValue("nak_alloy", out var nakIdx))
            {
                if (!_idToIndex.ContainsKey("nak")) _idToIndex["nak"] = nakIdx;
                if (!_idToIndex.ContainsKey("nak alloy")) _idToIndex["nak alloy"] = nakIdx;
                if (!_idToIndex.ContainsKey("nak_liquid")) _idToIndex["nak_liquid"] = nakIdx;
                if (!_idToIndex.ContainsKey("sodium_potassium")) _idToIndex["sodium_potassium"] = nakIdx;
                if (!_idToIndex.ContainsKey("sodium potassium")) _idToIndex["sodium potassium"] = nakIdx;
                if (!_idToIndex.ContainsKey("water_reactive_liquid")) _idToIndex["water_reactive_liquid"] = nakIdx;
            }
            if (_idToIndex.TryGetValue("liquid_caesium", out var liqCsIdx))
            {
                if (!_idToIndex.ContainsKey("liquid caesium")) _idToIndex["liquid caesium"] = liqCsIdx;
                if (!_idToIndex.ContainsKey("liquid_cesium")) _idToIndex["liquid_cesium"] = liqCsIdx;
                if (!_idToIndex.ContainsKey("liquid cesium")) _idToIndex["liquid cesium"] = liqCsIdx;
                if (!_idToIndex.ContainsKey("molten_caesium")) _idToIndex["molten_caesium"] = liqCsIdx;
                if (!_idToIndex.ContainsKey("molten caesium")) _idToIndex["molten caesium"] = liqCsIdx;
            }
            if (_idToIndex.TryGetValue("rubidium_liquid", out var rbIdx))
            {
                if (!_idToIndex.ContainsKey("rubidium")) _idToIndex["rubidium"] = rbIdx;
                if (!_idToIndex.ContainsKey("liquid rubidium")) _idToIndex["liquid rubidium"] = rbIdx;
                if (!_idToIndex.ContainsKey("liquid_rubidium")) _idToIndex["liquid_rubidium"] = rbIdx;
            }

            // High-temperature fire aliases
            if (_idToIndex.TryGetValue("blue_fire", out var blueFireIdx))
            {
                if (!_idToIndex.ContainsKey("blue fire")) _idToIndex["blue fire"] = blueFireIdx;
                if (!_idToIndex.ContainsKey("blue_flame")) _idToIndex["blue_flame"] = blueFireIdx;
                if (!_idToIndex.ContainsKey("blue flame")) _idToIndex["blue flame"] = blueFireIdx;
                if (!_idToIndex.ContainsKey("gas_fire")) _idToIndex["gas_fire"] = blueFireIdx;
                if (!_idToIndex.ContainsKey("gas fire")) _idToIndex["gas fire"] = blueFireIdx;
            }
            if (_idToIndex.TryGetValue("green_fire", out var greenFireIdx))
            {
                if (!_idToIndex.ContainsKey("green fire")) _idToIndex["green fire"] = greenFireIdx;
                if (!_idToIndex.ContainsKey("green_flame")) _idToIndex["green_flame"] = greenFireIdx;
                if (!_idToIndex.ContainsKey("green flame")) _idToIndex["green flame"] = greenFireIdx;
                if (!_idToIndex.ContainsKey("chemical_fire")) _idToIndex["chemical_fire"] = greenFireIdx;
                if (!_idToIndex.ContainsKey("chemical fire")) _idToIndex["chemical fire"] = greenFireIdx;
            }
            if (_idToIndex.TryGetValue("white_fire", out var whiteFireIdx))
            {
                if (!_idToIndex.ContainsKey("white fire")) _idToIndex["white fire"] = whiteFireIdx;
                if (!_idToIndex.ContainsKey("white_flame")) _idToIndex["white_flame"] = whiteFireIdx;
                if (!_idToIndex.ContainsKey("white flame")) _idToIndex["white flame"] = whiteFireIdx;
                if (!_idToIndex.ContainsKey("solar_fire")) _idToIndex["solar_fire"] = whiteFireIdx;
                if (!_idToIndex.ContainsKey("solar fire")) _idToIndex["solar fire"] = whiteFireIdx;
                if (!_idToIndex.ContainsKey("thermite_fire")) _idToIndex["thermite_fire"] = whiteFireIdx;
                if (!_idToIndex.ContainsKey("thermite fire")) _idToIndex["thermite fire"] = whiteFireIdx;
            }
            if (_idToIndex.TryGetValue("plasma_fire", out var plasmaFireIdx))
            {
                if (!_idToIndex.ContainsKey("plasma fire")) _idToIndex["plasma fire"] = plasmaFireIdx;
                if (!_idToIndex.ContainsKey("plasma_flame")) _idToIndex["plasma_flame"] = plasmaFireIdx;
                if (!_idToIndex.ContainsKey("plasma flame")) _idToIndex["plasma flame"] = plasmaFireIdx;
                if (!_idToIndex.ContainsKey("hyper_fire")) _idToIndex["hyper_fire"] = plasmaFireIdx;
                if (!_idToIndex.ContainsKey("hyper fire")) _idToIndex["hyper fire"] = plasmaFireIdx;
            }

            // Torch emitter aliases
            if (_idToIndex.TryGetValue("torch", out var torchIdx))
            {
                if (!_idToIndex.ContainsKey("fire_spout")) _idToIndex["fire_spout"] = torchIdx;
                if (!_idToIndex.ContainsKey("fire spout")) _idToIndex["fire spout"] = torchIdx;
                if (!_idToIndex.ContainsKey("fire_torch")) _idToIndex["fire_torch"] = torchIdx;
                if (!_idToIndex.ContainsKey("fire torch")) _idToIndex["fire torch"] = torchIdx;
                if (!_idToIndex.ContainsKey("torch_spout")) _idToIndex["torch_spout"] = torchIdx;
                if (!_idToIndex.ContainsKey("torch spout")) _idToIndex["torch spout"] = torchIdx;
            }
            if (_idToIndex.TryGetValue("blue_torch", out var blueTorchIdx))
            {
                if (!_idToIndex.ContainsKey("blue torch")) _idToIndex["blue torch"] = blueTorchIdx;
                if (!_idToIndex.ContainsKey("blue_fire_spout")) _idToIndex["blue_fire_spout"] = blueTorchIdx;
                if (!_idToIndex.ContainsKey("blue fire spout")) _idToIndex["blue fire spout"] = blueTorchIdx;
                if (!_idToIndex.ContainsKey("blue_fire_torch")) _idToIndex["blue_fire_torch"] = blueTorchIdx;
                if (!_idToIndex.ContainsKey("blue fire torch")) _idToIndex["blue fire torch"] = blueTorchIdx;
                if (!_idToIndex.ContainsKey("blue_spout")) _idToIndex["blue_spout"] = blueTorchIdx;
                if (!_idToIndex.ContainsKey("blue spout")) _idToIndex["blue spout"] = blueTorchIdx;
            }
            if (_idToIndex.TryGetValue("green_torch", out var greenTorchIdx))
            {
                if (!_idToIndex.ContainsKey("green torch")) _idToIndex["green torch"] = greenTorchIdx;
                if (!_idToIndex.ContainsKey("green_fire_spout")) _idToIndex["green_fire_spout"] = greenTorchIdx;
                if (!_idToIndex.ContainsKey("green fire spout")) _idToIndex["green fire spout"] = greenTorchIdx;
                if (!_idToIndex.ContainsKey("green_fire_torch")) _idToIndex["green_fire_torch"] = greenTorchIdx;
                if (!_idToIndex.ContainsKey("green fire torch")) _idToIndex["green fire torch"] = greenTorchIdx;
                if (!_idToIndex.ContainsKey("green_spout")) _idToIndex["green_spout"] = greenTorchIdx;
                if (!_idToIndex.ContainsKey("green spout")) _idToIndex["green spout"] = greenTorchIdx;
            }
            if (_idToIndex.TryGetValue("white_torch", out var whiteTorchIdx))
            {
                if (!_idToIndex.ContainsKey("white torch")) _idToIndex["white torch"] = whiteTorchIdx;
                if (!_idToIndex.ContainsKey("white_fire_spout")) _idToIndex["white_fire_spout"] = whiteTorchIdx;
                if (!_idToIndex.ContainsKey("white fire spout")) _idToIndex["white fire spout"] = whiteTorchIdx;
                if (!_idToIndex.ContainsKey("white_fire_torch")) _idToIndex["white_fire_torch"] = whiteTorchIdx;
                if (!_idToIndex.ContainsKey("white fire torch")) _idToIndex["white fire torch"] = whiteTorchIdx;
                if (!_idToIndex.ContainsKey("white_spout")) _idToIndex["white_spout"] = whiteTorchIdx;
                if (!_idToIndex.ContainsKey("white spout")) _idToIndex["white spout"] = whiteTorchIdx;
                if (!_idToIndex.ContainsKey("solar_torch")) _idToIndex["solar_torch"] = whiteTorchIdx;
            }
            if (_idToIndex.TryGetValue("plasma_torch", out var plasmaTorchIdx))
            {
                if (!_idToIndex.ContainsKey("plasma torch")) _idToIndex["plasma torch"] = plasmaTorchIdx;
                if (!_idToIndex.ContainsKey("plasma_fire_spout")) _idToIndex["plasma_fire_spout"] = plasmaTorchIdx;
                if (!_idToIndex.ContainsKey("plasma fire spout")) _idToIndex["plasma fire spout"] = plasmaTorchIdx;
                if (!_idToIndex.ContainsKey("plasma_fire_torch")) _idToIndex["plasma_fire_torch"] = plasmaTorchIdx;
                if (!_idToIndex.ContainsKey("plasma fire torch")) _idToIndex["plasma fire torch"] = plasmaTorchIdx;
                if (!_idToIndex.ContainsKey("plasma_spout")) _idToIndex["plasma_spout"] = plasmaTorchIdx;
                if (!_idToIndex.ContainsKey("plasma spout")) _idToIndex["plasma spout"] = plasmaTorchIdx;
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

                mat.IsTorch = mat.Definition.Id.Contains("torch", StringComparison.OrdinalIgnoreCase) ||
                              mat.Definition.Name.Contains("Torch", StringComparison.OrdinalIgnoreCase) ||
                              (mat.Definition.IsEmitter && mat.EmitsMaterialIndex > 0 && GetMaterial(mat.EmitsMaterialIndex).Definition.State == StateOfMatter.Energy);
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
