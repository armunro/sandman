using System;
using System.IO;
using System.Linq;
using Sandman.Core.Config;
using Sandman.Core.Models;
using Sandman.Core.Simulation;
using Xunit;

namespace Sandman.Tests
{
    public class SandmanTests
    {
        [Fact]
        public void DefaultRegistry_LoadsAllExpectedCategories()
        {
            var registry = MaterialRegistry.CreateDefault();

            Assert.True(registry.Count > 20, "Registry should contain multiple materials.");

            var iron = registry.TryGetMaterial("iron");
            Assert.NotNull(iron);
            Assert.Equal("Metals", iron.Definition.Category);
            Assert.Equal(StateOfMatter.Solid, iron.Definition.State);
            Assert.Equal(1538.0f, iron.Definition.MeltingPoint);

            var plastic = registry.TryGetMaterial("plastic");
            Assert.NotNull(plastic);
            Assert.Equal("Plastics", plastic.Definition.Category);

            var wood = registry.TryGetMaterial("wood");
            Assert.NotNull(wood);
            Assert.Equal("Woods", wood.Definition.Category);
            Assert.True(wood.Definition.IsFlammable);

            var stone = registry.TryGetMaterial("stone");
            Assert.NotNull(stone);
            Assert.Equal("Rocks", stone.Definition.Category);

            var water = registry.TryGetMaterial("water");
            Assert.NotNull(water);
            Assert.Equal("Liquids", water.Definition.Category);
            Assert.Equal(0.0f, water.Definition.FreezingPoint);
            Assert.Equal(100.0f, water.Definition.BoilingPoint);

            var gunpowder = registry.TryGetMaterial("gunpowder");
            Assert.NotNull(gunpowder);
            Assert.Equal("Explosives", gunpowder.Definition.Category);
            Assert.True(gunpowder.Definition.IsExplosive);

            var fire = registry.TryGetMaterial("fire");
            Assert.NotNull(fire);
            Assert.Equal("Flammables", fire.Definition.Category);

            var wall = registry.TryGetMaterial("wall");
            Assert.NotNull(wall);
            Assert.Equal("Tools", wall.Definition.Category);
        }

        [Fact]
        public void CustomYaml_LoadsSuccessfully()
        {
            string customYaml = @"
materials:
  - id: custom_metal
    name: Custom Metal
    category: Metals
    color: '#123456'
    state: Solid
    melting_point: 500.0
    melt_target: custom_liquid

  - id: custom_liquid
    name: Custom Liquid
    category: Liquids
    color: '#654321'
    state: Liquid
    freezing_point: 490.0
    freeze_target: custom_metal
";
            var registry = new MaterialRegistry();
            registry.LoadFromYaml(customYaml);

            Assert.Equal(3, registry.Count); // Air + 2 custom materials
            var metal = registry.TryGetMaterial("custom_metal");
            Assert.NotNull(metal);
            Assert.Equal(registry.GetIndex("custom_liquid"), metal.MeltTargetIndex);

            var liquid = registry.TryGetMaterial("custom_liquid");
            Assert.NotNull(liquid);
            Assert.Equal(registry.GetIndex("custom_metal"), liquid.FreezeTargetIndex);
        }

        [Fact]
        public void Sand_FallsUnderGravity()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            // Place sand at (5, 2)
            grid.SetCellById(5, 2, "sand");
            Assert.Equal("sand", registry.GetMaterial(grid.GetCell(5, 2).MaterialIndex).Definition.Id);

            // Step simulation 5 times
            for (int i = 0; i < 5; i++)
            {
                engine.Step();
            }

            // Sand should have fallen down towards (5, 7)
            Assert.True(grid.GetCell(5, 2).IsEmpty, "Original spot should be empty after falling.");
            Assert.False(grid.GetCell(5, 7).IsEmpty, "Sand should have moved down.");
        }

        [Fact]
        public void Sand_FallsOutOfCanvas_WhenFallThroughBottomEnabled()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid)
            {
                FallThroughBottom = true
            };

            // Place sand near bottom at (5, 8)
            grid.SetCellById(5, 8, "sand");
            Assert.False(grid.GetCell(5, 8).IsEmpty);

            // 1st step: falls to (5, 9)
            engine.Step();
            Assert.True(grid.GetCell(5, 8).IsEmpty);
            Assert.False(grid.GetCell(5, 9).IsEmpty);

            // 2nd step: falls out of bottom (y >= 10)
            engine.Step();
            Assert.True(grid.GetCell(5, 9).IsEmpty, "Sand should have fallen out of canvas.");
            Assert.Equal(0, grid.CountActiveParticles());
        }

        [Fact]
        public void Liquid_FallsOutOfCanvas_WhenFallThroughBottomEnabled()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid)
            {
                FallThroughBottom = true
            };

            // Place water near bottom at (5, 9)
            grid.SetCellById(5, 9, "water");
            Assert.False(grid.GetCell(5, 9).IsEmpty);

            // 1st step: water falls out of bottom
            engine.Step();
            Assert.True(grid.GetCell(5, 9).IsEmpty, "Water should have fallen out of canvas.");
            Assert.Equal(0, grid.CountActiveParticles());
        }

        [Fact]
        public void Gas_ExitsOutOfTopCanvas_WhenFalloutEnabled()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid)
            {
                Fallout = true
            };

            // Place gas at the very top row (5, 0)
            ushort gasIdx = registry.GetIndex("steam");
            grid.SetCell(5, 0, gasIdx);
            Assert.Equal(gasIdx, grid.GetCell(5, 0).MaterialIndex);

            // 1 step: gas rises out through top border (y < 0)
            engine.Step();
            Assert.True(grid.GetCell(5, 0).IsEmpty, "Gas should have exited out of the top of the canvas.");
            Assert.Equal(0, grid.CountActiveParticles());
        }

        [Fact]
        public void Liquid_ExitsOutOfLeftAndRightCanvas_WhenFalloutEnabled()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid)
            {
                Fallout = true
            };

            ushort wallIdx = registry.GetIndex("wall");
            ushort waterIdx = registry.GetIndex("water");

            // Build a solid floor at y=8 across all x to prevent bottom falling
            for (int x = 0; x < 10; x++)
            {
                grid.SetCell(x, 8, wallIdx);
            }

            // Place water at left edge (0, 7) and right edge (9, 7) resting on the wall floor
            grid.SetCell(0, 7, waterIdx);
            grid.SetCell(9, 7, waterIdx);

            // Step simulation: water disperses horizontally off the left (x < 0) and right (x >= 10) edges
            for (int i = 0; i < 5; i++)
            {
                engine.Step();
            }

            // Both water cells should have exited off the canvas sides
            int remainingWater = 0;
            for (int y = 0; y < 10; y++)
            {
                for (int x = 0; x < 10; x++)
                {
                    if (grid.GetCell(x, y).MaterialIndex == waterIdx) remainingWater++;
                }
            }

            Assert.Equal(0, remainingWater);
        }

        [Fact]
        public void Sand_ExitsOutOfSideDiagonals_WhenFalloutEnabled()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid)
            {
                Fallout = true
            };

            ushort wallIdx = registry.GetIndex("wall");
            ushort sandIdx = registry.GetIndex("sand");

            // Build a wall column on x=1 and floor at y=8 to funnel sand leftward out of bounds
            grid.SetCell(0, 8, wallIdx);
            grid.SetCell(1, 8, wallIdx);
            grid.SetCell(0, 7, sandIdx); // sand at (0, 7) resting on wall at (0, 8) with obstacle at (1, 7)

            // When sand tries to slide diagonally left to (-1, 8), it exits through the left side
            engine.Step();

            int remainingSand = 0;
            for (int y = 0; y < 10; y++)
            {
                for (int x = 0; x < 10; x++)
                {
                    if (grid.GetCell(x, y).MaterialIndex == sandIdx) remainingSand++;
                }
            }

            Assert.Equal(0, remainingSand);
        }

        [Fact]
        public void Energy_ExitsOutOfCanvas_WhenFalloutEnabled()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid)
            {
                Fallout = true
            };

            ushort fireIdx = registry.GetIndex("fire");

            // Place fire at top row (5, 0)
            grid.SetCell(5, 0, fireIdx);

            // Step simulation: fire flickers upward into y < 0 and exits
            for (int i = 0; i < 5; i++)
            {
                engine.Step();
            }

            // Fire should have exited or decayed out
            Assert.True(grid.GetCell(5, 0).IsEmpty);
        }

        [Fact]
        public void Fallout_Disabled_KeepsParticlesContainedWithinBorders()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid)
            {
                Fallout = false
            };

            ushort sandIdx = registry.GetIndex("sand");
            ushort waterIdx = registry.GetIndex("water");
            ushort steamIdx = registry.GetIndex("steam");

            // Place sand and water at bottom, steam at top
            grid.SetCell(3, 9, sandIdx);
            grid.SetCell(6, 9, waterIdx);
            grid.SetCell(5, 0, steamIdx);

            engine.Step();

            // Sand stays at bottom row (3, 9)
            Assert.Equal(sandIdx, grid.GetCell(3, 9).MaterialIndex);
            // Steam stays at top row (or horizontal top row)
            bool steamAtTop = false;
            for (int x = 0; x < 10; x++)
            {
                if (grid.GetCell(x, 0).MaterialIndex == steamIdx) steamAtTop = true;
            }
            Assert.True(steamAtTop, "Steam should remain contained at top when fallout is disabled.");
        }

        [Fact]
        public void Liquid_FallsAndDisperses()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            // Place water puddle at bottom center (10, 19)
            grid.SetCellById(10, 19, "water");
            grid.SetCellById(10, 18, "water");

            // Step simulation
            for (int i = 0; i < 10; i++)
            {
                engine.Step();
            }

            // The water at (10, 18) should disperse horizontally onto the floor (y=19)
            int waterOnFloor = 0;
            for (int x = 0; x < 20; x++)
            {
                if (grid.GetCell(x, 19).MaterialIndex == registry.GetIndex("water"))
                    waterOnFloor++;
            }

            Assert.Equal(2, waterOnFloor);
        }

        [Fact]
        public void Density_HeavierSandSinksInWater()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort waterIdx = registry.GetIndex("water");
            ushort sandIdx = registry.GetIndex("sand");
            ushort wallIdx = registry.GetIndex("wall");

            // Create a 1-wide column with walls on left and right and bottom
            for (int y = 5; y <= 9; y++)
            {
                grid.SetCell(4, y, wallIdx);
                grid.SetCell(6, y, wallIdx);
            }
            grid.SetCell(5, 9, wallIdx); // Floor

            // Put water at bottom of column (5, 8)
            grid.SetCell(5, 8, waterIdx);
            // Put sand directly above water (5, 7)
            grid.SetCell(5, 7, sandIdx);

            engine.Step();

            // Sand should sink below water into (5, 8), water is displaced up to (5, 7)
            Assert.Equal(sandIdx, grid.GetCell(5, 8).MaterialIndex);
            Assert.Equal(waterIdx, grid.GetCell(5, 7).MaterialIndex);
        }

        [Fact]
        public void Gas_RisesUpward()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            // Place methane gas at bottom (5, 8)
            ushort gasIdx = registry.GetIndex("methane_gas");
            grid.SetCell(5, 8, gasIdx);

            for (int i = 0; i < 6; i++)
            {
                engine.Step();
            }

            // Gas should have moved upward
            Assert.True(grid.GetCell(5, 8).IsEmpty);
            bool foundAbove = false;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 10; x++)
                {
                    if (grid.GetCell(x, y).MaterialIndex == gasIdx)
                        foundAbove = true;
                }
            }
            Assert.True(foundAbove, "Gas should have risen upward.");
        }

        [Fact]
        public void Thermodynamics_HeatTransfersBetweenCells()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort ironIdx = registry.GetIndex("iron");
            grid.SetCell(4, 5, ironIdx, temperature: 200.0f);
            grid.SetCell(5, 5, ironIdx, temperature: 20.0f);

            for (int i = 0; i < 5; i++)
            {
                engine.Step();
            }

            float tempHot = grid.GetCell(4, 5).Temperature;
            float tempCold = grid.GetCell(5, 5).Temperature;

            Assert.True(tempHot < 200.0f, "Hot iron should lose heat.");
            Assert.True(tempCold > 20.0f, "Cold iron should gain heat.");
        }

        [Fact]
        public void PhaseChange_IceMeltsToWater_WaterBoilsToSteam()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort iceIdx = registry.GetIndex("ice");
            ushort waterIdx = registry.GetIndex("water");
            ushort steamIdx = registry.GetIndex("steam");
            ushort wallIdx = registry.GetIndex("wall");

            // Place a sealed crucible container around (5, 5)
            grid.SetCell(4, 5, wallIdx);
            grid.SetCell(6, 5, wallIdx);
            grid.SetCell(4, 6, wallIdx);
            grid.SetCell(5, 6, wallIdx);
            grid.SetCell(6, 6, wallIdx);

            // Ice heated above 0°C
            grid.SetCell(5, 5, iceIdx, temperature: 25.0f);
            engine.Step();

            Assert.Equal(waterIdx, grid.GetCell(5, 5).MaterialIndex);

            // Water heated above 100°C
            grid.GetCell(5, 5).Temperature = 120.0f;
            engine.Step();

            // After boiling, steam is formed (and may rise to (5, 4))
            bool foundSteam = grid.GetCell(5, 5).MaterialIndex == steamIdx || grid.GetCell(5, 4).MaterialIndex == steamIdx;
            Assert.True(foundSteam, "Water should have boiled into steam.");
        }

        [Fact]
        public void PhaseChange_MetalMeltsAndFreezes()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort ironIdx = registry.GetIndex("iron");
            ushort moltenIronIdx = registry.GetIndex("molten_iron");
            ushort wallIdx = registry.GetIndex("wall");

            // Place a sealed crucible container around (5, 5)
            grid.SetCell(4, 5, wallIdx);
            grid.SetCell(6, 5, wallIdx);
            grid.SetCell(4, 6, wallIdx);
            grid.SetCell(5, 6, wallIdx);
            grid.SetCell(6, 6, wallIdx);

            // Iron heated above 1538°C melts
            grid.SetCell(5, 5, ironIdx, temperature: 1600.0f);
            engine.Step();

            Assert.Equal(moltenIronIdx, grid.GetCell(5, 5).MaterialIndex);

            // Molten iron cooled below 1538°C solidifies
            grid.GetCell(5, 5).Temperature = 1400.0f;
            engine.Step();

            Assert.Equal(ironIdx, grid.GetCell(5, 5).MaterialIndex);
        }

        [Fact]
        public void Combustion_WoodIgnitesAndBurns()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort woodIdx = registry.GetIndex("wood");

            // Wood heated above ignition temperature (280°C)
            grid.SetCell(5, 5, woodIdx, temperature: 350.0f);
            engine.Step();

            ref var cell = ref grid.GetCell(5, 5);
            Assert.True(cell.IsBurning, "Wood should be burning when above ignition temp.");

            // Run next step to let combustion heat generate
            engine.Step();
            Assert.True(grid.GetCell(5, 5).Temperature > 300.0f, "Burning wood should stay hot.");
        }

        [Fact]
        public void Explosion_GunpowderDetonatesAndClearsArea()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(30, 30, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort sandIdx = registry.GetIndex("sand");
            ushort gunpowderIdx = registry.GetIndex("gunpowder");

            // Fill a block of sand
            grid.DrawBox(10, 10, 20, 20, sandIdx, filled: true);
            int initialSandCount = grid.CountActiveParticles();

            // Place gunpowder in center and ignite
            grid.SetCell(15, 15, gunpowderIdx, temperature: 500.0f);
            engine.Step();

            // Sand count should have drastically decreased due to blast destruction
            int remainingSandCount = 0;
            for (int i = 0; i < grid.Cells.Length; i++)
            {
                if (grid.Cells[i].MaterialIndex == sandIdx)
                    remainingSandCount++;
            }

            Assert.True(remainingSandCount < initialSandCount, "Explosion should destroy sand particles.");
        }

        [Fact]
        public void Explosion_ChainDetonation()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(40, 40, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort tntIdx = registry.GetIndex("tnt");
            ushort c4Idx = registry.GetIndex("c4");

            // Place TNT and nearby C4
            grid.SetCell(15, 15, tntIdx);
            grid.SetCell(18, 15, c4Idx);

            // Detonate TNT
            engine.DetonateAt(15, 15, tntIdx);

            // Both explosives should have detonated and cleared
            Assert.True(grid.GetCell(15, 15).MaterialIndex != tntIdx, "TNT should detonate.");
            Assert.True(grid.GetCell(18, 15).MaterialIndex != c4Idx, "C4 should chain detonate.");
        }

        [Fact]
        public void Chemical_AcidCorrodesMaterials()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort acidIdx = registry.GetIndex("acid");
            ushort plantIdx = registry.GetIndex("plant");

            // Place plant and acid next to each other
            grid.SetCell(5, 5, plantIdx);
            grid.SetCell(5, 4, acidIdx);

            for (int i = 0; i < 10; i++)
            {
                engine.Step();
            }

            // Plant should be dissolved
            Assert.True(grid.GetCell(5, 5).IsEmpty || grid.GetCell(5, 5).MaterialIndex != plantIdx, "Acid should corrode plant.");
        }

        [Fact]
        public void SpoutEmitter_GeneratesMaterial()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort spoutIdx = registry.GetIndex("water_spout");
            ushort waterIdx = registry.GetIndex("water");

            grid.SetCell(5, 2, spoutIdx);

            for (int i = 0; i < 10; i++)
            {
                engine.Step();
            }

            int waterCount = 0;
            for (int i = 0; i < grid.Cells.Length; i++)
            {
                if (grid.Cells[i].MaterialIndex == waterIdx)
                    waterCount++;
            }

            Assert.True(waterCount > 0, "Water spout should generate water particles.");
        }

        [Fact]
        public void Drain_ConsumesSurroundingParticles()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort drainIdx = registry.GetIndex("drain");
            ushort sandIdx = registry.GetIndex("sand");

            grid.SetCell(5, 5, drainIdx);
            grid.SetCell(5, 4, sandIdx);
            grid.SetCell(4, 5, sandIdx);

            engine.Step();

            Assert.True(grid.GetCell(5, 4).IsEmpty, "Drain should delete sand at (5, 4).");
            Assert.True(grid.GetCell(4, 5).IsEmpty, "Drain should delete sand at (4, 5).");
        }

        [Fact]
        public void Shapes_DrawBoxAndEllipse()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);

            ushort stoneIdx = registry.GetIndex("stone");

            // Draw filled box
            grid.DrawBox(2, 2, 6, 6, stoneIdx, filled: true);
            Assert.Equal(25, grid.CountActiveParticles());

            // Clear and draw filled ellipse
            grid.Clear();
            grid.DrawEllipse(2, 2, 8, 8, stoneIdx, filled: true);
            Assert.True(grid.CountActiveParticles() > 0);
            Assert.Equal(stoneIdx, grid.GetCell(5, 5).MaterialIndex);
        }

        [Fact]
        public void Shapes_DrawBox_RespectsBrushThickness()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            ushort stoneIdx = registry.GetIndex("stone");

            // 10x10 box outline with thickness = 1 (from 0,0 to 9,9)
            grid.DrawBox(0, 0, 9, 9, stoneIdx, filled: false, thickness: 1);
            Assert.Equal(36, grid.CountActiveParticles()); // 100 - (8*8) = 36
            Assert.True(grid.GetCell(5, 5).IsEmpty);
            Assert.Equal(stoneIdx, grid.GetCell(0, 0).MaterialIndex);
            Assert.True(grid.GetCell(1, 1).IsEmpty);

            // Clear and draw with thickness = 2
            grid.Clear();
            grid.DrawBox(0, 0, 9, 9, stoneIdx, filled: false, thickness: 2);
            Assert.Equal(64, grid.CountActiveParticles()); // 100 - (6*6) = 64
            Assert.True(grid.GetCell(5, 5).IsEmpty);
            Assert.Equal(stoneIdx, grid.GetCell(0, 0).MaterialIndex);
            Assert.Equal(stoneIdx, grid.GetCell(1, 1).MaterialIndex);
            Assert.True(grid.GetCell(2, 2).IsEmpty);

            // Clear and draw with thickness = 3
            grid.Clear();
            grid.DrawBox(0, 0, 9, 9, stoneIdx, filled: false, thickness: 3);
            Assert.Equal(84, grid.CountActiveParticles()); // 100 - (4*4) = 84
            Assert.True(grid.GetCell(5, 5).IsEmpty);
            Assert.Equal(stoneIdx, grid.GetCell(2, 2).MaterialIndex);
            Assert.True(grid.GetCell(3, 3).IsEmpty);
        }

        [Fact]
        public void Shapes_DrawEllipse_RespectsBrushThickness()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(30, 30, registry, seed: 42);
            ushort stoneIdx = registry.GetIndex("stone");

            // Ellipse outline with thickness = 1
            grid.DrawEllipse(2, 2, 26, 26, stoneIdx, filled: false, thickness: 1);
            int count1 = grid.CountActiveParticles();
            Assert.True(grid.GetCell(14, 14).IsEmpty, "Center of outline circle should be empty");

            // Ellipse outline with thickness = 3
            grid.Clear();
            grid.DrawEllipse(2, 2, 26, 26, stoneIdx, filled: false, thickness: 3);
            int count3 = grid.CountActiveParticles();
            Assert.True(count3 > count1, "Thicker brush outline should place more particles than thinner brush outline");
            Assert.True(grid.GetCell(14, 14).IsEmpty, "Center of outline circle should still be empty");

            // Ellipse filled
            grid.Clear();
            grid.DrawEllipse(2, 2, 26, 26, stoneIdx, filled: true, thickness: 3);
            int countFilled = grid.CountActiveParticles();
            Assert.True(countFilled > count3, "Filled circle should have more particles than outline circle");
            Assert.False(grid.GetCell(14, 14).IsEmpty, "Center of filled circle should have particles");
        }

        [Fact]
        public void Thermodynamics_HeatedAirDissipatesToAmbient()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            // Heat an area of air up to 500°C
            grid.GetCell(5, 5).Temperature = 500.0f;
            grid.GetCell(5, 6).Temperature = 500.0f;

            // Step simulation for 60 ticks
            for (int i = 0; i < 60; i++)
            {
                engine.Step();
            }

            // Temperature should have significantly dissipated towards ambient (20°C)
            float temp = grid.GetCell(5, 5).Temperature;
            Assert.True(temp < 100.0f, $"Heated air should dissipate quickly. Actual: {temp}°C");
        }

        [Fact]
        public void Thermodynamics_HeatedMaterialDissipatesToAmbient()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort sandIdx = registry.GetIndex("sand");
            grid.SetCell(5, 9, sandIdx, temperature: 400.0f);

            // Step simulation for 100 ticks
            for (int i = 0; i < 100; i++)
            {
                engine.Step();
            }

            float temp = grid.GetCell(5, 9).Temperature;
            Assert.True(temp < 250.0f, $"Heated sand should cool down over time. Actual: {temp}°C");
        }

        [Fact]
        public void Thermodynamics_FixedTemperatureHeaterWarmsAdjacentCells()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort heaterIdx = registry.GetIndex("heater");
            ushort ironIdx = registry.GetIndex("iron");

            grid.SetCell(5, 5, heaterIdx); // 600°C fixed temperature
            grid.SetCell(6, 5, ironIdx, temperature: 20.0f);

            for (int i = 0; i < 20; i++)
            {
                engine.Step();
            }

            // Heater should stay at 600°C, and iron neighbor should heat up
            Assert.Equal(600.0f, grid.GetCell(5, 5).Temperature);
            Assert.True(grid.GetCell(6, 5).Temperature > 50.0f, $"Iron next to heater should warm up. Actual: {grid.GetCell(6, 5).Temperature}°C");
        }

        [Fact]
        public void Thermodynamics_CooledAreaWarmsToAmbient()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            grid.GetCell(5, 5).Temperature = -150.0f;

            for (int i = 0; i < 60; i++)
            {
                engine.Step();
            }

            float temp = grid.GetCell(5, 5).Temperature;
            Assert.True(temp > -20.0f, $"Sub-zero cooled area should warm back up towards ambient. Actual: {temp}°C");
        }

        [Fact]
        public void Thermodynamics_FallingHotMoltenMetal_DoesNotSolidifyInstantly()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(15, 30, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort moltenIronIdx = registry.GetIndex("molten_iron");
            ushort ironIdx = registry.GetIndex("iron");

            // Drop molten iron from the top
            grid.SetCell(7, 0, moltenIronIdx); // Default temp 1650°C, freezing point 1538°C

            // Step 10 times - particle should fall 10 units through air
            for (int i = 0; i < 10; i++)
            {
                engine.Step();
            }

            // Find where the particle fell across the grid
            int foundX = -1;
            int foundY = -1;
            for (int y = 0; y < 30; y++)
            {
                for (int x = 0; x < 15; x++)
                {
                    if (!grid.GetCell(x, y).IsEmpty)
                    {
                        foundX = x;
                        foundY = y;
                        break;
                    }
                }
                if (foundY != -1) break;
            }

            Assert.True(foundY >= 8, $"Molten iron should have fallen downwards. Found at Y={foundY}");
            ref var particle = ref grid.GetCell(foundX, foundY);
            Assert.Equal(moltenIronIdx, particle.MaterialIndex);
            Assert.True(particle.Temperature > 1538.0f, $"Molten iron should stay above freezing point (1538°C) while falling. Actual: {particle.Temperature}°C");
        }

        [Fact]
        public void Thermodynamics_SolidHotObject_DiffusesHeatIntoAmbientEnvironment()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(25, 25, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort heaterIdx = registry.GetIndex("heater"); // 600°C fixed temperature

            // Place a heater at the center
            grid.SetCell(12, 12, heaterIdx);

            // Step 30 times
            for (int i = 0; i < 30; i++)
            {
                engine.Step();
            }

            // Check air cells 1, 2, and 3 pixels away
            float temp1Px = grid.GetCell(13, 12).Temperature; // 1 pixel right
            float temp2Px = grid.GetCell(14, 12).Temperature; // 2 pixels right
            float temp3Px = grid.GetCell(15, 12).Temperature; // 3 pixels right
            float tempAbove = grid.GetCell(12, 11).Temperature; // 1 pixel above

            Assert.True(temp1Px > 50.0f, $"Air 1 pixel away should heat up significantly. Actual: {temp1Px}°C");
            Assert.True(temp2Px > 30.0f, $"Air 2 pixels away should heat up above ambient. Actual: {temp2Px}°C");
            Assert.True(temp3Px > 21.0f, $"Heat should diffuse multiple pixels into ambient environment. Actual: {temp3Px}°C");
            Assert.True(tempAbove >= temp1Px, $"Upward convection should give strong warming above hot object. Above={tempAbove}°C, Right={temp1Px}°C");
        }

        [Fact]
        public void Thermodynamics_HotSolidNonFixed_DissipatesHeatIntoAirAndCoolsDown()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(25, 25, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort ironIdx = registry.GetIndex("iron"); // Solid iron, non-fixed temperature

            // Place a hot iron particle at 600°C surrounded by ambient air (20°C)
            grid.SetCell(12, 12, ironIdx, 600.0f);

            float initialIronTemp = grid.GetCell(12, 12).Temperature;
            Assert.Equal(600.0f, initialIronTemp);

            // Step 15 times
            for (int i = 0; i < 15; i++)
            {
                engine.Step();
            }

            float currentIronTemp = grid.GetCell(12, 12).Temperature;
            float airAboveTemp = grid.GetCell(12, 11).Temperature;
            float airRightTemp = grid.GetCell(13, 12).Temperature;

            // Verify the hot solid dissipates heat into surrounding air
            Assert.True(airAboveTemp > 35.0f, $"Air above hot solid should warm up significantly from dissipated heat. Actual: {airAboveTemp}°C");
            Assert.True(airRightTemp > 25.0f, $"Air beside hot solid should warm up from dissipated heat. Actual: {airRightTemp}°C");
            Assert.True(airAboveTemp >= airRightTemp, $"Upward convection should produce a warmer air column above the hot solid. Above={airAboveTemp}°C, Right={airRightTemp}°C");

            // Verify the hot solid cools down over time
            Assert.True(currentIronTemp < initialIronTemp - 50.0f, $"Hot solid should cool down over time as it dissipates heat into the air. Initial={initialIronTemp}°C, Current={currentIronTemp}°C");
        }

        [Fact]
        public void Thermodynamics_HotMovableSolid_DissipatesHeatIntoAir()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort sandIdx = registry.GetIndex("sand"); // Movable solid

            // Place a block of hot sand at 450°C
            grid.DrawSquare(10, 15, 3, sandIdx, 450.0f);

            // Step 20 times
            for (int i = 0; i < 20; i++)
            {
                engine.Step();
            }

            // Air above the hot sand should be warmed
            float airAbove = grid.GetCell(10, 11).Temperature;
            Assert.True(airAbove > 25.0f, $"Air above hot sand block should warm up from dissipated heat. Actual: {airAbove}°C");

            // Hot sand should have cooled down
            float sandTemp = grid.GetCell(10, 15).Temperature;
            Assert.True(sandTemp < 440.0f, $"Hot sand should cool down as it dissipates heat. Actual: {sandTemp}°C");
        }

        [Fact]
        public void Thermodynamics_FallingLava_MaintainsLiquidStateWhileFalling()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(15, 30, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort lavaIdx = registry.GetIndex("lava");
            ushort basaltIdx = registry.GetIndex("basalt");

            // Drop lava from top
            grid.SetCell(7, 0, lavaIdx);

            for (int i = 0; i < 15; i++)
            {
                engine.Step();
            }

            int foundX = -1;
            int foundY = -1;
            for (int y = 0; y < 30; y++)
            {
                for (int x = 0; x < 15; x++)
                {
                    if (!grid.GetCell(x, y).IsEmpty)
                    {
                        foundX = x;
                        foundY = y;
                        break;
                    }
                }
                if (foundY != -1) break;
            }

            Assert.True(foundY >= 10, $"Lava should have fallen down. Found at Y={foundY}");
            ref var particle = ref grid.GetCell(foundX, foundY);
            Assert.Equal(lavaIdx, particle.MaterialIndex);
            Assert.True(particle.Temperature > 750.0f, $"Lava should maintain temperature above freezing point (750°C). Actual: {particle.Temperature}°C");
        }

        [Fact]
        public void Thermodynamics_SurroundingPixels_DissipateToAmbientEntropy()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(25, 25, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            // Heat an area of surrounding air and stone pixels to 400°C
            ushort stoneIdx = registry.GetIndex("stone");
            grid.SetCell(12, 12, stoneIdx, 400.0f);
            grid.GetCell(11, 12).Temperature = 400.0f; // Air left
            grid.GetCell(13, 12).Temperature = 400.0f; // Air right
            grid.GetCell(12, 11).Temperature = 400.0f; // Air above
            grid.GetCell(12, 13).Temperature = 400.0f; // Air below

            // Run simulation for 120 steps to observe thermodynamic entropy
            for (int i = 0; i < 120; i++)
            {
                engine.Step();
            }

            // Surrounding pixels should have significantly cooled towards ambient (20°C)
            float airLeft = grid.GetCell(11, 12).Temperature;
            float airRight = grid.GetCell(13, 12).Temperature;
            float airAbove = grid.GetCell(12, 11).Temperature;
            float stoneTemp = grid.GetCell(12, 12).Temperature;

            Assert.True(airLeft < 400.0f, $"Surrounding air should exchange heat with environment. Actual: {airLeft}°C");
            Assert.True(airRight < 400.0f, $"Surrounding air should exchange heat with environment. Actual: {airRight}°C");
            Assert.True(airAbove < 400.0f, $"Surrounding air column should exchange heat with environment. Actual: {airAbove}°C");
            Assert.True(stoneTemp < 400.0f, $"Surrounding stone should conduct heat away. Actual: {stoneTemp}°C");
        }

        [Fact]
        public void Thermodynamics_AllMaterialCategories_ObeyEntropyDissipation()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(30, 30, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort ironIdx = registry.GetIndex("iron");
            ushort stoneIdx = registry.GetIndex("stone");
            ushort woodIdx = registry.GetIndex("wood");

            // Place heated materials in distinct isolated cells
            grid.SetCell(5, 15, ironIdx, 500.0f);
            grid.SetCell(15, 15, stoneIdx, 500.0f);
            grid.SetCell(25, 15, woodIdx, 250.0f); // Under ignition temp

            // Step 150 times
            for (int i = 0; i < 150; i++)
            {
                engine.Step();
            }

            // All materials should have cooled down significantly following entropy
            float ironTemp = grid.GetCell(5, 15).Temperature;
            float stoneTemp = grid.GetCell(15, 15).Temperature;
            float woodTemp = grid.GetCell(25, 15).Temperature;

            Assert.True(ironTemp < 500.0f, $"Iron should have dissipated heat. Actual: {ironTemp}°C");
            Assert.True(stoneTemp < 500.0f, $"Stone should have dissipated heat. Actual: {stoneTemp}°C");
            Assert.True(woodTemp < 250.0f, $"Wood should have dissipated heat. Actual: {woodTemp}°C");
        }

        [Fact]
        public void Thermodynamics_HighTemperatureSolids_NormalizeToRoomTemperature()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(30, 30, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort ironIdx = registry.GetIndex("iron");
            ushort stoneIdx = registry.GetIndex("stone");
            ushort basaltIdx = registry.GetIndex("basalt");
            ushort glassIdx = registry.GetIndex("glass");

            // Place various solids at very high temperatures (600°C - 1000°C)
            grid.SetCell(5, 15, ironIdx, 1000.0f);
            grid.SetCell(10, 15, stoneIdx, 800.0f);
            grid.SetCell(15, 15, basaltIdx, 700.0f);
            grid.SetCell(20, 15, glassIdx, 600.0f);

            // Step for 200 ticks (~3.3 seconds in real-time)
            for (int i = 0; i < 200; i++)
            {
                engine.Step();
            }

            // All solids should dissipate heat into surrounding air
            float ironTemp = grid.GetCell(5, 15).Temperature;
            float stoneTemp = grid.GetCell(10, 15).Temperature;
            float basaltTemp = grid.GetCell(15, 15).Temperature;
            float glassTemp = grid.GetCell(20, 15).Temperature;

            Assert.True(ironTemp < 1000.0f, $"Iron should cool down. Actual: {ironTemp}°C");
            Assert.True(stoneTemp < 800.0f, $"Stone should cool down. Actual: {stoneTemp}°C");
            Assert.True(basaltTemp < 700.0f, $"Basalt should cool down. Actual: {basaltTemp}°C");
            Assert.True(glassTemp < 600.0f, $"Glass should cool down. Actual: {glassTemp}°C");
        }

        [Fact]
        public void Thermodynamics_SolidBlockAtHighTemperature_NormalizesToRoomTemperature()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(30, 30, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort ironIdx = registry.GetIndex("iron");

            // Create a 5x5 block of solid iron at 900°C
            grid.DrawSquare(15, 15, 5, ironIdx, 900.0f);

            // Verify initial center cell temperature
            Assert.Equal(900.0f, grid.GetCell(15, 15).Temperature);

            // Step for 240 ticks (~4 seconds in real-time)
            for (int i = 0; i < 240; i++)
            {
                engine.Step();
            }

            // Verify all cells in the block dissipated heat over time
            float centerTemp = grid.GetCell(15, 15).Temperature;
            float cornerTemp = grid.GetCell(13, 13).Temperature;
            Assert.True(centerTemp < 900.0f, $"Block center should cool down. Actual: {centerTemp}°C");
            Assert.True(cornerTemp < centerTemp, $"Corner should cool faster than center. Corner: {cornerTemp}°C, Center: {centerTemp}°C");
        }

        [Fact]
        public void Thermodynamics_HighTemperatureMovableSolids_NormalizeToRoomTemperature()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(25, 25, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort sandIdx = registry.GetIndex("sand");

            // Place a pile of hot sand at 500°C
            grid.DrawSquare(12, 20, 5, sandIdx, 500.0f);

            // Step for 220 ticks
            for (int i = 0; i < 220; i++)
            {
                engine.Step();
            }

            // Verify sand cooled down as heat diffuses into air
            for (int y = 18; y < 25; y++)
            {
                for (int x = 10; x <= 14; x++)
                {
                    ref var cell = ref grid.GetCell(x, y);
                    if (cell.MaterialIndex == sandIdx)
                    {
                        Assert.True(cell.Temperature < 500.0f, $"Sand should cool down. Actual: {cell.Temperature}°C");
                    }
                }
            }
        }

        [Fact]
        public void Thermodynamics_SolidMaterial_RadiatesAndDissipatesOverTime()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort ironIdx = registry.GetIndex("iron");
            grid.SetCell(10, 10, ironIdx, 1200.0f);

            float initialTemp = grid.GetCell(10, 10).Temperature;
            Assert.Equal(1200.0f, initialTemp);

            // Run 30 steps: should drop
            for (int i = 0; i < 30; i++) engine.Step();
            float t30 = grid.GetCell(10, 10).Temperature;
            Assert.True(t30 < initialTemp, $"Temperature should decrease after 30 steps. Got {t30}°C");

            // Run another 30 steps: should continue to drop
            for (int i = 0; i < 30; i++) engine.Step();
            float t60 = grid.GetCell(10, 10).Temperature;
            Assert.True(t60 < t30, $"Temperature should continue decreasing after 60 steps. Got {t60}°C");

            // Run another 80 steps: should continue decreasing
            for (int i = 0; i < 80; i++) engine.Step();
            float t140 = grid.GetCell(10, 10).Temperature;
            Assert.True(t140 < t60, $"Temperature should continue decreasing after 140 steps. Got {t140}°C");
        }

        [Fact]
        public void Thermodynamics_MoltenMetalSolidification_CoolsAndRadiatesHeatContinuously()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort moltenIronIdx = registry.GetIndex("molten_iron");
            ushort solidIronIdx = registry.GetIndex("iron");
            ushort wallIdx = registry.GetIndex("wall");

            // Build small basin to contain molten liquid
            grid.SetCell(9, 19, wallIdx);
            grid.SetCell(11, 19, wallIdx);
            grid.SetCell(10, 19, moltenIronIdx, 1750.0f);

            // Step until it cools and freezes into solid iron
            int stepsToSolid = 0;
            while (grid.GetCell(10, 19).MaterialIndex == moltenIronIdx && stepsToSolid < 300)
            {
                engine.Step();
                stepsToSolid++;
            }

            // Verify it froze into solid iron
            Assert.Equal(solidIronIdx, grid.GetCell(10, 19).MaterialIndex);
            float frozenTemp = grid.GetCell(10, 19).Temperature;
            Assert.True(frozenTemp <= 1538.0f, $"Solid iron initial temperature should be <= freezing point. Got {frozenTemp}°C");

            // Step further and verify solid iron continues to dissipate heat
            for (int i = 0; i < 150; i++)
            {
                engine.Step();
            }

            float finalTemp = grid.GetCell(10, 19).Temperature;
            Assert.True(finalTemp < frozenTemp, $"Solid iron should continue dissipating heat. Got {finalTemp}°C vs {frozenTemp}°C");
        }

        [Fact]
        public void Thermodynamics_LavaMeltsIron()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort ironIdx = registry.GetIndex("iron");
            ushort lavaIdx = registry.GetIndex("lava");
            ushort moltenIronIdx = registry.GetIndex("molten_iron");
            ushort wallIdx = registry.GetIndex("wall");

            // Build a container with tall walls to contain the lava pool
            grid.DrawBox(7, 11, 13, 17, wallIdx, filled: false);

            // Place iron in bottom of container surrounded by hot lava pool
            grid.SetCell(10, 16, ironIdx);
            for (int x = 8; x <= 12; x++)
            {
                if (x != 10) grid.SetCell(x, 16, lavaIdx);
                grid.SetCell(x, 15, lavaIdx);
                grid.SetCell(x, 14, lavaIdx);
                grid.SetCell(x, 13, lavaIdx);
                grid.SetCell(x, 12, lavaIdx);
            }

            // Step engine
            bool melted = false;
            for (int i = 0; i < 120; i++)
            {
                engine.Step();
                if (grid.GetCell(10, 16).MaterialIndex == moltenIronIdx || grid.GetCell(10, 16).MaterialIndex == lavaIdx)
                {
                    melted = true;
                    break;
                }
            }

            Assert.True(melted, "Lava should transfer sufficient heat to melt adjacent iron into molten iron.");
        }

        [Fact]
        public void Thermodynamics_LavaMeltsThroughRock()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort stoneIdx = registry.GetIndex("stone");
            ushort lavaIdx = registry.GetIndex("lava");
            ushort wallIdx = registry.GetIndex("wall");

            // Build a container with a rock floor layer
            grid.DrawBox(7, 10, 13, 17, wallIdx, filled: false);
            // Place rock / stone layer across the middle from wall to wall
            for (int x = 8; x <= 12; x++)
            {
                grid.SetCell(x, 14, stoneIdx);
            }

            // Place lava on top of the rock layer
            for (int x = 8; x <= 12; x++)
            {
                grid.SetCell(x, 13, lavaIdx);
                grid.SetCell(x, 12, lavaIdx);
                grid.SetCell(x, 11, lavaIdx);
            }

            // Step engine
            bool rockMelted = false;
            for (int i = 0; i < 120; i++)
            {
                engine.Step();
                // Check if the stone layer melted into lava or allowed lava to flow down through
                if (grid.GetCell(10, 14).MaterialIndex == lavaIdx || grid.GetCell(10, 15).MaterialIndex == lavaIdx || grid.GetCell(10, 14).IsEmpty)
                {
                    rockMelted = true;
                    break;
                }
            }

            Assert.True(rockMelted, "Hot lava should transfer heat to melt through adjacent rock/stone.");
        }

        [Fact]
        public void Fuses_LoadedInRegistryWithExpectedProperties()
        {
            var registry = MaterialRegistry.CreateDefault();

            var slowFuse = registry.TryGetMaterial("slow_fuse");
            var stdFuse = registry.TryGetMaterial("fuse");
            var fastFuse = registry.TryGetMaterial("fast_fuse");
            var instFuse = registry.TryGetMaterial("instant_fuse");
            var powderFuse = registry.TryGetMaterial("powder_fuse");
            var waterproofFuse = registry.TryGetMaterial("waterproof_fuse");

            Assert.NotNull(slowFuse);
            Assert.NotNull(stdFuse);
            Assert.NotNull(fastFuse);
            Assert.NotNull(instFuse);
            Assert.NotNull(powderFuse);
            Assert.NotNull(waterproofFuse);

            Assert.True(slowFuse.Definition.IsFlammable);
            Assert.True(stdFuse.Definition.IsFlammable);
            Assert.True(fastFuse.Definition.IsFlammable);
            Assert.True(instFuse.Definition.IsFlammable);
            Assert.True(powderFuse.Definition.IsFlammable);
            Assert.True(waterproofFuse.Definition.IsFlammable);

            Assert.True(instFuse.Definition.IsInstantFuse);
            Assert.True(waterproofFuse.Definition.IsWaterproof);
            Assert.Equal(StateOfMatter.MovableSolid, powderFuse.Definition.State);
            Assert.Equal(StateOfMatter.Solid, slowFuse.Definition.State);
            Assert.Equal(StateOfMatter.Solid, stdFuse.Definition.State);
            Assert.Equal(StateOfMatter.Solid, fastFuse.Definition.State);
            Assert.Equal(StateOfMatter.Solid, instFuse.Definition.State);
            Assert.Equal(StateOfMatter.Solid, waterproofFuse.Definition.State);

            // Aliases
            Assert.Equal(stdFuse.Index, registry.GetIndex("standard_fuse"));
            Assert.Equal(stdFuse.Index, registry.GetIndex("normal_fuse"));
            Assert.Equal(slowFuse.Index, registry.GetIndex("delay_fuse"));
            Assert.Equal(fastFuse.Index, registry.GetIndex("quick_fuse"));
            Assert.Equal(instFuse.Index, registry.GetIndex("det_cord"));
        }

        [Fact]
        public void Fuses_BurnAtVariousSpeeds()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(40, 20, registry, seed: 123);
            var engine = new SimulationEngine(grid);

            ushort fastFuseIdx = registry.GetIndex("fast_fuse");
            ushort stdFuseIdx = registry.GetIndex("fuse");
            ushort slowFuseIdx = registry.GetIndex("slow_fuse");
            ushort sparkIdx = registry.GetIndex("spark");

            int length = 15;
            int startX = 5;
            int endX = startX + length;

            // Draw three parallel horizontal lines of fuses
            // Row 4: Fast Fuse
            // Row 9: Standard Fuse
            // Row 14: Slow Fuse
            for (int x = startX; x <= endX; x++)
            {
                grid.SetCell(x, 4, fastFuseIdx);
                grid.SetCell(x, 9, stdFuseIdx);
                grid.SetCell(x, 14, slowFuseIdx);
            }

            // Ignite start of each fuse simultaneously by placing a spark at startX - 1
            grid.SetCell(startX - 1, 4, sparkIdx, 1000.0f);
            grid.SetCell(startX - 1, 9, sparkIdx, 1000.0f);
            grid.SetCell(startX - 1, 14, sparkIdx, 1000.0f);

            int fastReachStep = -1;
            int stdReachStep = -1;
            int slowReachStep = -1;

            for (int step = 1; step <= 250; step++)
            {
                engine.Step();

                if (fastReachStep == -1 && (grid.GetCell(endX, 4).IsBurning || grid.GetCell(endX, 4).IsEmpty))
                {
                    fastReachStep = step;
                }
                if (stdReachStep == -1 && (grid.GetCell(endX, 9).IsBurning || grid.GetCell(endX, 9).IsEmpty))
                {
                    stdReachStep = step;
                }
                if (slowReachStep == -1 && (grid.GetCell(endX, 14).IsBurning || grid.GetCell(endX, 14).IsEmpty))
                {
                    slowReachStep = step;
                }

                if (fastReachStep != -1 && stdReachStep != -1 && slowReachStep != -1)
                    break;
            }

            Assert.True(fastReachStep > 0, "Fast fuse should reach the end");
            Assert.True(stdReachStep > 0, "Standard fuse should reach the end");
            Assert.True(slowReachStep > 0, "Slow fuse should reach the end");

            // Fast fuse must be faster than standard fuse, which must be faster than slow fuse
            Assert.True(fastReachStep < stdReachStep, $"Fast fuse ({fastReachStep} steps) should burn faster than Standard fuse ({stdReachStep} steps)");
            Assert.True(stdReachStep < slowReachStep, $"Standard fuse ({stdReachStep} steps) should burn faster than Slow fuse ({slowReachStep} steps)");
        }

        [Fact]
        public void Fuses_InstantFuse_PropagatesInstantaneouslyAndDetonates()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(30, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort instantFuseIdx = registry.GetIndex("instant_fuse");
            ushort tntIdx = registry.GetIndex("tnt");
            ushort sparkIdx = registry.GetIndex("spark");

            // Draw an instant fuse line leading to TNT
            for (int x = 5; x <= 20; x++)
            {
                grid.SetCell(x, 10, instantFuseIdx);
            }
            grid.SetCell(21, 10, tntIdx);

            // Ignite the start of the instant fuse adjacent to it
            grid.SetCell(4, 10, sparkIdx, 1000.0f);

            // Step once
            engine.Step();

            // The TNT at the far end of the instant fuse should be detonated immediately
            Assert.NotEqual(tntIdx, grid.GetCell(21, 10).MaterialIndex);
        }

        [Fact]
        public void Fuses_WaterproofFuse_BurnsUnderwater()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort wpFuseIdx = registry.GetIndex("waterproof_fuse");
            ushort waterIdx = registry.GetIndex("water");
            ushort wallIdx = registry.GetIndex("wall");

            // Build a small water tank
            grid.DrawBox(5, 8, 15, 16, wallIdx, filled: false);
            grid.DrawBox(6, 9, 14, 15, waterIdx, filled: true);

            // Place waterproof fuse line through the water
            grid.DrawLine(7, 12, 13, 12, 1, wpFuseIdx);

            // Ignite the first fuse cell
            ref var cell = ref grid.GetCell(7, 12);
            cell.SetBurning(true);
            cell.Temperature = 500.0f;
            cell.Life = 20;

            // Step engine
            engine.Step();

            // Cell should still be burning and not extinguished by water
            Assert.True(grid.GetCell(7, 12).IsBurning, "Waterproof fuse should remain burning even when submerged in water.");
        }

        [Fact]
        public void Fuses_PowderFuse_FallsAndBurns()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort powderFuseIdx = registry.GetIndex("powder_fuse");
            ushort sparkIdx = registry.GetIndex("spark");
            ushort wallIdx = registry.GetIndex("wall");

            // Place wide flat floor so powder does not slide off single post
            for (int x = 8; x <= 12; x++)
            {
                grid.SetCell(x, 8, wallIdx);
            }

            // Place powder fuse in midair
            grid.SetCell(10, 5, powderFuseIdx);

            // Step: it should fall down due to gravity
            engine.Step();
            Assert.Equal(powderFuseIdx, grid.GetCell(10, 6).MaterialIndex);

            // Step until it settles on floor at y=7
            engine.Step();
            Assert.Equal(powderFuseIdx, grid.GetCell(10, 7).MaterialIndex);

            // Ignite it with spark at (10, 6)
            grid.SetCell(10, 6, sparkIdx, 1000.0f);
            engine.Step();

            ref var cell = ref grid.GetCell(10, 7);
            Assert.True(cell.IsBurning || cell.Temperature > 80.0f, "Powder fuse should ignite and burn.");
        }

        [Fact]
        public void Fire_DecaysToAirWithoutSmoke()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort fireIdx = registry.GetIndex("fire");
            ushort smokeIdx = registry.GetIndex("smoke");

            grid.SetCell(5, 5, fireIdx);

            // Step through fire's entire lifetime (with variance buffer)
            for (int i = 0; i < 60; i++)
            {
                engine.Step();
            }

            // Verify no smoke was created and fire dissipated into air
            for (int y = 0; y < 10; y++)
            {
                for (int x = 0; x < 10; x++)
                {
                    Assert.NotEqual(smokeIdx, grid.GetCell(x, y).MaterialIndex);
                }
            }
        }

        [Fact]
        public void Fire_DecaysGraduallyNotAllAtOnce()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 123);
            var engine = new SimulationEngine(grid);

            ushort fireIdx = registry.GetIndex("fire");

            // Draw a block of fire
            grid.DrawSquare(10, 10, 6, fireIdx);
            int initialCount = grid.CountActiveParticles();
            Assert.True(initialCount >= 20, "Should have created multiple fire particles");

            // Check that lifetimes have variance
            var lifetimes = new HashSet<ushort>();
            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    if (grid.GetCell(x, y).MaterialIndex == fireIdx)
                    {
                        lifetimes.Add(grid.GetCell(x, y).Life);
                    }
                }
            }
            Assert.True(lifetimes.Count > 3, "Fire particles should have varied lifetimes");

            // Track counts at different milestones
            int distinctCountsObserved = 0;
            int lastCount = initialCount;

            for (int tick = 0; tick < 60; tick++)
            {
                engine.Step();
                int currentCount = grid.CountActiveParticles();
                if (currentCount != lastCount)
                {
                    distinctCountsObserved++;
                    lastCount = currentCount;
                }
                if (currentCount == 0)
                    break;
            }

            // It should decay across multiple distinct steps, not all at once
            Assert.True(distinctCountsObserved >= 4, $"Decay should occur across multiple frames. Observed count changes: {distinctCountsObserved}");
            Assert.Equal(0, grid.CountActiveParticles());
        }

        [Fact]
        public void Combustion_BurningFuelHasVariance()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort woodIdx = registry.GetIndex("wood");

            // Set multiple wood cells with high temperature exceeding ignition temperature (300°C)
            for (int x = 1; x <= 8; x++)
            {
                grid.SetCell(x, 5, woodIdx, temperature: 400.0f);
            }

            // Step 1 tick to trigger ignition
            engine.Step();

            // Check burning wood cells
            var burningLifes = new HashSet<ushort>();
            for (int x = 1; x <= 8; x++)
            {
                ref var cell = ref grid.GetCell(x, 5);
                if (cell.IsBurning)
                {
                    burningLifes.Add(cell.Life);
                }
            }

            // Burning fuel was assigned with randomized jitter
            Assert.True(burningLifes.Count > 1, $"Burning cells should have varied fuel/lifetimes. Found {burningLifes.Count} distinct values.");
        }

        [Fact]
        public void BinaryExplosive_LoadedInRegistryWithExpectedProperties()
        {
            var registry = MaterialRegistry.CreateDefault();
            var bexpA = registry.TryGetMaterial("binary_explosive_a");
            var bexpB = registry.TryGetMaterial("binary_explosive_b");

            Assert.NotNull(bexpA);
            Assert.NotNull(bexpB);

            Assert.Equal("Explosives", bexpA.Definition.Category);
            Assert.Equal("Explosives", bexpB.Definition.Category);

            Assert.True(bexpA.Definition.IsExplosive);
            Assert.True(bexpB.Definition.IsExplosive);

            Assert.True(bexpA.Definition.ExplodesOnReaction);
            Assert.True(bexpB.Definition.ExplodesOnReaction);

            Assert.Equal("binary_explosive_b", bexpA.Definition.ReactsWith);
            Assert.Equal("binary_explosive_a", bexpB.Definition.ReactsWith);

            Assert.Equal(bexpB.Index, bexpA.ReactsWithIndex);
            Assert.Equal(bexpA.Index, bexpB.ReactsWithIndex);

            // Verify alias resolution
            Assert.Equal(bexpA.Index, registry.GetIndex("binary_explosive"));
            Assert.Equal(bexpA.Index, registry.GetIndex("bexp"));
            Assert.Equal(bexpA.Index, registry.GetIndex("bexp_a"));
            Assert.Equal(bexpB.Index, registry.GetIndex("bexp_b"));
        }

        [Fact]
        public void BinaryExplosive_StableWhenIsolated()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort bexpA = registry.GetIndex("binary_explosive_a");
            ushort bexpB = registry.GetIndex("binary_explosive_b");

            // Place isolated A and B particles on the floor in separate columns without touching
            grid.SetCell(2, 9, bexpA);
            grid.SetCell(7, 9, bexpB);

            for (int i = 0; i < 30; i++)
            {
                engine.Step();
            }

            // Both should remain stable and intact on the floor
            Assert.Equal(bexpA, grid.GetCell(2, 9).MaterialIndex);
            Assert.Equal(bexpB, grid.GetCell(7, 9).MaterialIndex);
            Assert.Equal(2, grid.CountActiveParticles());
        }

        [Fact]
        public void BinaryExplosive_DetonatesOnContact()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort bexpA = registry.GetIndex("binary_explosive_a");
            ushort bexpB = registry.GetIndex("binary_explosive_b");
            ushort woodIdx = registry.GetIndex("wood");

            // Surround with wood barrier
            for (int x = 2; x <= 18; x++)
            {
                grid.SetCell(x, 15, woodIdx);
            }

            // Place A and B directly next to each other
            grid.SetCell(10, 10, bexpA);
            grid.SetCell(11, 10, bexpB);

            // Step 1 tick to trigger contact reaction and explosion
            engine.Step();

            // The contact should have detonated both explosives and blasted wood below
            Assert.NotEqual(bexpA, grid.GetCell(10, 10).MaterialIndex);
            Assert.NotEqual(bexpB, grid.GetCell(11, 10).MaterialIndex);
            Assert.True(grid.GetCell(10, 10).Temperature > 500.0f, "Detonation point should be hot from blast");
        }

        [Fact]
        public void BinaryExplosive_FallingContactTriggersExplosion()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort bexpA = registry.GetIndex("binary_explosive_a");
            ushort bexpB = registry.GetIndex("binary_explosive_b");

            // Place A at the bottom (solid ground at y=19)
            grid.SetCell(10, 19, bexpA);

            // Drop B from above at y=10
            grid.SetCell(10, 10, bexpB);

            // Step simulation until B falls onto A and detonates
            bool exploded = false;
            for (int i = 0; i < 20; i++)
            {
                engine.Step();
                // Check if high temperature / fire / cleared cell indicates explosion
                if (grid.GetCell(10, 19).Temperature > 300.0f || grid.GetCell(10, 19).MaterialIndex != bexpA)
                {
                    exploded = true;
                    break;
                }
            }

            Assert.True(exploded, "Falling binary explosive B onto binary explosive A should cause contact detonation.");
        }

        [Fact]
        public void BinaryExplosive_IgnitedByExtremeHeat()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort bexpA = registry.GetIndex("binary_explosive_a");

            // Place Binary Explosive A with temperature exceeding its 350°C ignition point
            grid.SetCell(10, 10, bexpA, temperature: 400.0f);

            engine.Step();

            // Should have detonated from thermal ignition
            Assert.NotEqual(bexpA, grid.GetCell(10, 10).MaterialIndex);

            // Verify explosion produced heat/fire in the area
            float maxTemp = 0;
            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    if (grid.GetCell(x, y).Temperature > maxTemp)
                        maxTemp = grid.GetCell(x, y).Temperature;
                }
            }

            Assert.True(maxTemp > 300.0f, $"Detonation should deposit high heat in the area. Max temp: {maxTemp}°C");
        }

        [Fact]
        public void MaterialRegistry_SearchAndCategoryFilterQueries_WorkAsExpected()
        {
            var registry = MaterialRegistry.CreateDefault();
            var materials = registry.Materials.Where(m => m.Index != MaterialRegistry.EmptyIndex).ToList();

            // All expected categories should be present
            var categories = materials.Select(m => m.Definition.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Assert.Contains("Metals", categories, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Rocks", categories, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Liquids", categories, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Explosives", categories, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Flammables", categories, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Woods", categories, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Plastics", categories, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("Tools", categories, StringComparer.OrdinalIgnoreCase);

            // Category filtering
            var metals = materials.Where(m => m.Definition.Category.Equals("Metals", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Contains(metals, m => m.Definition.Id == "iron");
            Assert.Contains(metals, m => m.Definition.Id == "copper");
            Assert.Contains(metals, m => m.Definition.Id == "gold");

            var explosives = materials.Where(m => m.Definition.Category.Equals("Explosives", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Contains(explosives, m => m.Definition.Id == "gunpowder");
            Assert.Contains(explosives, m => m.Definition.Id == "tnt");
            Assert.Contains(explosives, m => m.Definition.Id == "c4");
            Assert.Contains(explosives, m => m.Definition.Id == "binary_explosive_a");
            Assert.Contains(explosives, m => m.Definition.Id == "binary_explosive_b");

            // Text search
            var searchNitro = materials.Where(m => m.Definition.Name.Contains("Nitro", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.Contains(searchNitro, m => m.Definition.Id == "nitroglycerin");
            Assert.Contains(searchNitro, m => m.Definition.Id == "liquid_nitrogen");

            var searchMolten = materials.Where(m => m.Definition.Name.Contains("Molten", StringComparison.OrdinalIgnoreCase)).ToList();
            Assert.True(searchMolten.Count >= 4, "Should find multiple molten metal materials.");
        }

        [Fact]
        public void Thermodynamics_CustomAmbientTemperature_MaterialsAndAirEquilibrateToNewAmbient()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            // Set ambient temperature to 60°C with air update
            grid.SetAmbientTemperature(60.0f, updateAirCells: true);
            Assert.Equal(60.0f, grid.AmbientTemperature);
            Assert.Equal(60.0f, engine.AmbientTemperature);
            Assert.Equal(60.0f, grid.GetCell(5, 5).Temperature);

            ushort ironIdx = registry.GetIndex("iron");
            // Place a cool iron at 20°C and hot iron at 120°C
            grid.SetCell(5, 10, ironIdx, temperature: 20.0f);
            grid.SetCell(15, 10, ironIdx, temperature: 120.0f);

            // Step simulation
            for (int i = 0; i < 150; i++)
            {
                engine.Step();
            }

            // Both should equilibrate towards the 60°C ambient temperature
            float coolIronTemp = grid.GetCell(5, 10).Temperature;
            float hotIronTemp = grid.GetCell(15, 10).Temperature;

            Assert.True(coolIronTemp > 35.0f, $"Cool iron should warm towards 60°C ambient. Actual: {coolIronTemp}°C");
            Assert.True(hotIronTemp < 95.0f, $"Hot iron should cool towards 60°C ambient. Actual: {hotIronTemp}°C");
        }

        [Fact]
        public void Thermodynamics_TemperatureDecayRate_DisabledConservesHeat()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid)
            {
                TemperatureDecayRate = 0.0f // Decay disabled
            };

            ushort ironIdx = registry.GetIndex("iron");
            grid.DrawSquare(10, 10, 5, ironIdx, temperature: 500.0f);

            // Run 50 steps
            for (int i = 0; i < 50; i++)
            {
                engine.Step();
            }

            // Sum the total thermal energy in the grid
            float totalHeat = 0.0f;
            for (int i = 0; i < grid.Cells.Length; i++)
            {
                totalHeat += grid.Cells[i].Temperature;
            }

            // Heat should diffuse across cells without being destroyed by ambient dissipation
            Assert.True(grid.GetCell(10, 10).Temperature > 100.0f, "Iron block center should retain high heat when decay is disabled.");
            Assert.True(totalHeat > 20.0f * 400 + 1000.0f, "Total thermal energy should remain conserved across the grid without ambient decay.");
        }

        [Fact]
        public void Thermodynamics_TemperatureDecayRate_FasterDecayCoolsFaster()
        {
            var registry = MaterialRegistry.CreateDefault();
            
            // Simulation with custom settings
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid) { TemperatureDecayRate = 1.0f };
            ushort ironIdx = registry.GetIndex("iron");
            grid.SetCell(10, 10, ironIdx, temperature: 800.0f);

            // Step for 40 ticks
            for (int i = 0; i < 40; i++)
            {
                engine.Step();
            }

            float temp = grid.GetCell(10, 10).Temperature;
            Assert.True(temp < 800.0f, $"Iron should dissipate heat into surrounding air. Got: {temp}°C");
        }

        [Fact]
        public void UndoRedo_GridSnapshot_CorrectlyCapturesAndRestoresGridState()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            ushort stoneIdx = registry.GetIndex("stone");

            grid.DrawSquare(5, 5, 4, stoneIdx);
            grid.SetAmbientTemperature(35.0f);
            var snapshot = grid.CreateSnapshot();

            // Mutate grid
            grid.Clear();
            grid.SetAmbientTemperature(0.0f);
            Assert.True(snapshot.HasDifferences(grid));
            Assert.Equal(0, grid.CountActiveParticles());

            // Restore from snapshot
            snapshot.RestoreTo(grid);
            Assert.False(snapshot.HasDifferences(grid));
            Assert.Equal(35.0f, grid.AmbientTemperature);
            Assert.True(grid.CountActiveParticles() > 0);
            Assert.Equal(stoneIdx, grid.GetCell(5, 5).MaterialIndex);
        }

        [Fact]
        public void UndoRedo_UndoAndRedoSingleAction_RevertsAndRestoresState()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var undoRedo = new UndoRedoManager();
            ushort woodIdx = registry.GetIndex("wood");

            Assert.False(undoRedo.CanUndo);
            Assert.False(undoRedo.CanRedo);

            // Record state before drawing
            undoRedo.RecordBeforeChange(grid);
            grid.DrawBox(2, 2, 8, 8, woodIdx, filled: true);
            int particleCountAfterDraw = grid.CountActiveParticles();
            Assert.True(particleCountAfterDraw > 0);
            Assert.True(undoRedo.CanUndo);
            Assert.False(undoRedo.CanRedo);

            // Undo
            bool undone = undoRedo.Undo(grid);
            Assert.True(undone);
            Assert.Equal(0, grid.CountActiveParticles());
            Assert.False(undoRedo.CanUndo);
            Assert.True(undoRedo.CanRedo);

            // Redo
            bool redone = undoRedo.Redo(grid);
            Assert.True(redone);
            Assert.Equal(particleCountAfterDraw, grid.CountActiveParticles());
            Assert.True(undoRedo.CanUndo);
            Assert.False(undoRedo.CanRedo);
        }

        [Fact]
        public void UndoRedo_MultipleShapeDrawings_UndoStepByStepAndRedo()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(30, 30, registry, seed: 42);
            var undoRedo = new UndoRedoManager();
            ushort wallIdx = registry.GetIndex("wall");
            ushort waterIdx = registry.GetIndex("water");
            ushort fireIdx = registry.GetIndex("fire");

            // Step 1: Draw Wall Box
            undoRedo.RecordBeforeChange(grid);
            grid.DrawBox(2, 2, 10, 10, wallIdx, filled: false);
            int count1 = grid.CountActiveParticles();

            // Step 2: Draw Water Ellipse
            undoRedo.RecordBeforeChange(grid);
            grid.DrawEllipse(15, 15, 25, 25, waterIdx, filled: true);
            int count2 = grid.CountActiveParticles();

            // Step 3: Draw Line of Fire
            undoRedo.RecordBeforeChange(grid);
            grid.DrawLine(0, 0, 29, 0, 1, fireIdx);
            int count3 = grid.CountActiveParticles();

            Assert.True(count3 > count2);
            Assert.True(count2 > count1);
            Assert.Equal(3, undoRedo.UndoCount);
            Assert.Equal(0, undoRedo.RedoCount);

            // Undo Step 3 (Fire line removed)
            undoRedo.Undo(grid);
            Assert.Equal(count2, grid.CountActiveParticles());
            Assert.True(grid.GetCell(0, 0).IsEmpty);

            // Undo Step 2 (Water ellipse removed)
            undoRedo.Undo(grid);
            Assert.Equal(count1, grid.CountActiveParticles());
            Assert.True(grid.GetCell(20, 20).IsEmpty);

            // Undo Step 1 (Wall box removed)
            undoRedo.Undo(grid);
            Assert.Equal(0, grid.CountActiveParticles());
            Assert.False(undoRedo.CanUndo);
            Assert.Equal(3, undoRedo.RedoCount);

            // Redo all 3
            undoRedo.Redo(grid);
            Assert.Equal(count1, grid.CountActiveParticles());

            undoRedo.Redo(grid);
            Assert.Equal(count2, grid.CountActiveParticles());

            undoRedo.Redo(grid);
            Assert.Equal(count3, grid.CountActiveParticles());
            Assert.False(undoRedo.CanRedo);
        }

        [Fact]
        public void UndoRedo_NewActionAfterUndo_ClearsRedoStack()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var undoRedo = new UndoRedoManager();
            ushort sandIdx = registry.GetIndex("sand");
            ushort acidIdx = registry.GetIndex("acid");
            ushort lavaIdx = registry.GetIndex("lava");

            // Action 1
            undoRedo.RecordBeforeChange(grid);
            grid.DrawSquare(5, 5, 2, sandIdx);

            // Action 2
            undoRedo.RecordBeforeChange(grid);
            grid.DrawSquare(15, 15, 2, acidIdx);

            Assert.Equal(2, undoRedo.UndoCount);

            // Undo Action 2
            undoRedo.Undo(grid);
            Assert.True(undoRedo.CanRedo);
            Assert.Equal(1, undoRedo.RedoCount);

            // New Action 3 replaces redo history
            undoRedo.RecordBeforeChange(grid);
            grid.DrawSquare(10, 10, 2, lavaIdx);

            Assert.False(undoRedo.CanRedo);
            Assert.Equal(0, undoRedo.RedoCount);
            Assert.Equal(2, undoRedo.UndoCount);

            // Undo should now revert Action 3, leaving Action 1
            undoRedo.Undo(grid);
            Assert.Equal(sandIdx, grid.GetCell(5, 5).MaterialIndex);
            Assert.True(grid.GetCell(10, 10).IsEmpty);
        }

        [Fact]
        public void UndoRedo_MaxHistoryLimit_TrimsOldestSnapshots()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var undoRedo = new UndoRedoManager { MaxHistory = 3 };
            ushort sandIdx = registry.GetIndex("sand");

            for (int i = 0; i < 6; i++)
            {
                undoRedo.RecordBeforeChange(grid);
                grid.SetCell(i, i, sandIdx);
            }

            Assert.Equal(3, undoRedo.UndoCount);

            // We can only undo 3 times
            Assert.True(undoRedo.Undo(grid));
            Assert.True(undoRedo.Undo(grid));
            Assert.True(undoRedo.Undo(grid));
            Assert.False(undoRedo.CanUndo);
            Assert.False(undoRedo.Undo(grid));
        }

        [Fact]
        public void UndoRedo_Clear_ResetsUndoAndRedoStacks()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry, seed: 42);
            var undoRedo = new UndoRedoManager();
            ushort sandIdx = registry.GetIndex("sand");

            undoRedo.RecordBeforeChange(grid);
            grid.SetCell(1, 1, sandIdx);

            undoRedo.Undo(grid);
            Assert.True(undoRedo.CanRedo);

            undoRedo.Clear();
            Assert.False(undoRedo.CanUndo);
            Assert.False(undoRedo.CanRedo);
            Assert.Equal(0, undoRedo.UndoCount);
            Assert.Equal(0, undoRedo.RedoCount);
        }

        [Fact]
        public void UndoRedo_SandPlacementAndPhysics_UndoRevertsToPriorState()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(15, 15, registry, seed: 42);
            var engine = new SimulationEngine(grid);
            var undoRedo = new UndoRedoManager();
            ushort sandIdx = registry.GetIndex("sand");

            undoRedo.RecordBeforeChange(grid);
            grid.DrawCircle(7, 2, 2, sandIdx);

            // Step simulation so sand falls
            for (int i = 0; i < 8; i++)
            {
                engine.Step();
            }

            // Undo reverts grid back to blank state before sand was placed
            undoRedo.Undo(grid);
            Assert.Equal(0, grid.CountActiveParticles());

            // Redo restores falling sand state
            undoRedo.Redo(grid);
            Assert.True(grid.CountActiveParticles() > 0);
        }

        [Fact]
        public void WaterReactiveExplosives_LoadedInRegistryWithExpectedProperties()
        {
            var registry = MaterialRegistry.CreateDefault();
            var naPowder = registry.TryGetMaterial("sodium_powder");
            var kPowder = registry.TryGetMaterial("potassium_powder");
            var sodium = registry.TryGetMaterial("sodium");
            var potassium = registry.TryGetMaterial("potassium");
            var caesium = registry.TryGetMaterial("caesium");
            var nak = registry.TryGetMaterial("nak_alloy");
            var liqCs = registry.TryGetMaterial("liquid_caesium");
            var rubidium = registry.TryGetMaterial("rubidium_liquid");

            // Verify existence
            Assert.NotNull(naPowder);
            Assert.NotNull(kPowder);
            Assert.NotNull(sodium);
            Assert.NotNull(potassium);
            Assert.NotNull(caesium);
            Assert.NotNull(nak);
            Assert.NotNull(liqCs);
            Assert.NotNull(rubidium);

            // Verify states
            Assert.Equal(StateOfMatter.MovableSolid, naPowder.Definition.State);
            Assert.Equal(StateOfMatter.MovableSolid, kPowder.Definition.State);
            Assert.Equal(StateOfMatter.Solid, sodium.Definition.State);
            Assert.Equal(StateOfMatter.Solid, potassium.Definition.State);
            Assert.Equal(StateOfMatter.Solid, caesium.Definition.State);
            Assert.Equal(StateOfMatter.Liquid, nak.Definition.State);
            Assert.Equal(StateOfMatter.Liquid, liqCs.Definition.State);
            Assert.Equal(StateOfMatter.Liquid, rubidium.Definition.State);

            // Verify category
            Assert.Equal("Explosives", naPowder.Definition.Category);
            Assert.Equal("Explosives", kPowder.Definition.Category);
            Assert.Equal("Explosives", sodium.Definition.Category);
            Assert.Equal("Explosives", potassium.Definition.Category);
            Assert.Equal("Explosives", caesium.Definition.Category);
            Assert.Equal("Explosives", nak.Definition.Category);
            Assert.Equal("Explosives", liqCs.Definition.Category);
            Assert.Equal("Explosives", rubidium.Definition.Category);

            // Verify explosive & water reaction properties
            ushort waterIdx = registry.GetIndex("water");
            Assert.True(waterIdx > 0);

            var list = new[] { naPowder, kPowder, sodium, potassium, caesium, nak, liqCs, rubidium };
            foreach (var mat in list)
            {
                Assert.True(mat.Definition.IsExplosive, $"{mat.Definition.Id} should be explosive");
                Assert.True(mat.Definition.ExplodesOnReaction, $"{mat.Definition.Id} should explode on reaction");
                Assert.Equal("water", mat.Definition.ReactsWith);
                Assert.Equal(waterIdx, mat.ReactsWithIndex);
            }

            // Verify aliases
            Assert.Equal(naPowder.Index, registry.GetIndex("sodium powder"));
            Assert.Equal(naPowder.Index, registry.GetIndex("sodium_dust"));
            Assert.Equal(naPowder.Index, registry.GetIndex("water_reactive_powder"));
            Assert.Equal(kPowder.Index, registry.GetIndex("potassium powder"));
            Assert.Equal(sodium.Index, registry.GetIndex("metallic_sodium"));
            Assert.Equal(sodium.Index, registry.GetIndex("water_reactive_solid"));
            Assert.Equal(potassium.Index, registry.GetIndex("metallic_potassium"));
            Assert.Equal(caesium.Index, registry.GetIndex("cesium"));
            Assert.Equal(caesium.Index, registry.GetIndex("metallic_caesium"));
            Assert.Equal(nak.Index, registry.GetIndex("nak"));
            Assert.Equal(nak.Index, registry.GetIndex("nak alloy"));
            Assert.Equal(nak.Index, registry.GetIndex("water_reactive_liquid"));
            Assert.Equal(liqCs.Index, registry.GetIndex("liquid caesium"));
            Assert.Equal(liqCs.Index, registry.GetIndex("liquid_cesium"));
            Assert.Equal(rubidium.Index, registry.GetIndex("liquid rubidium"));
        }

        [Fact]
        public void WaterReactiveExplosives_StableWhenIsolatedFromWater()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort naPowder = registry.GetIndex("sodium_powder");
            ushort sodium = registry.GetIndex("sodium");
            ushort nak = registry.GetIndex("nak_alloy");
            ushort wall = registry.GetIndex("wall");

            // Place isolated on ground (contained so liquid doesn't flow off)
            grid.SetCell(2, 19, sodium);
            grid.SetCell(8, 19, naPowder);

            grid.SetCell(13, 19, wall);
            grid.SetCell(14, 19, nak);
            grid.SetCell(15, 19, wall);

            for (int i = 0; i < 30; i++)
            {
                engine.Step();
            }

            // Stable without water
            Assert.Equal(sodium, grid.GetCell(2, 19).MaterialIndex);
            Assert.Equal(naPowder, grid.GetCell(8, 19).MaterialIndex);
            Assert.Equal(nak, grid.GetCell(14, 19).MaterialIndex);
        }

        [Fact]
        public void WaterReactivePowder_ExplodesOnContactWithWater()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort naPowder = registry.GetIndex("sodium_powder");
            ushort water = registry.GetIndex("water");
            ushort wood = registry.GetIndex("wood");

            // Wood floor
            for (int x = 0; x < 20; x++)
            {
                grid.SetCell(x, 18, wood);
            }

            // Place powder directly next to water
            grid.SetCell(10, 10, naPowder);
            grid.SetCell(11, 10, water);

            engine.Step();

            // Contact should detonate powder and produce blast heat
            Assert.NotEqual(naPowder, grid.GetCell(10, 10).MaterialIndex);
            float maxTemp = 0;
            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    if (grid.GetCell(x, y).Temperature > maxTemp)
                        maxTemp = grid.GetCell(x, y).Temperature;
                }
            }
            Assert.True(maxTemp > 400.0f, $"Powder detonation with water should deposit extreme blast heat. Found {maxTemp}°C");
        }

        [Fact]
        public void WaterReactivePowder_FallingOntoWaterDetonates()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort kPowder = registry.GetIndex("potassium_powder");
            ushort water = registry.GetIndex("water");

            // Water pool at bottom
            grid.SetCell(10, 19, water);

            // Drop potassium powder from above
            grid.SetCell(10, 12, kPowder);

            bool exploded = false;
            for (int i = 0; i < 20; i++)
            {
                engine.Step();
                if (grid.GetCell(10, 19).Temperature > 300.0f || grid.GetCell(10, 19).MaterialIndex != water)
                {
                    exploded = true;
                    break;
                }
            }

            Assert.True(exploded, "Falling potassium powder onto water should detonate on impact.");
        }

        [Fact]
        public void WaterReactiveSolid_ExplodesWhenWaterPouredOntoIt()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort caesium = registry.GetIndex("caesium");
            ushort water = registry.GetIndex("water");

            // Solid Caesium sitting on bottom
            grid.SetCell(10, 19, caesium);

            // Water falling from above
            grid.SetCell(10, 12, water);

            bool exploded = false;
            for (int i = 0; i < 20; i++)
            {
                engine.Step();
                if (grid.GetCell(10, 19).Temperature > 300.0f || grid.GetCell(10, 19).MaterialIndex != caesium)
                {
                    exploded = true;
                    break;
                }
            }

            Assert.True(exploded, "Dropping water onto solid caesium should cause explosive detonation.");
        }

        [Fact]
        public void WaterReactiveLiquid_ExplodesOnContactWithWater()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort nak = registry.GetIndex("nak_alloy");
            ushort water = registry.GetIndex("water");
            ushort wood = registry.GetIndex("wood");

            // Wood barrier below
            for (int x = 0; x < 20; x++)
            {
                grid.SetCell(x, 15, wood);
            }

            // Place NaK and Water directly adjacent
            grid.SetCell(10, 10, nak);
            grid.SetCell(11, 10, water);

            engine.Step();

            // Contact should detonate NaK liquid and produce blast heat
            Assert.NotEqual(nak, grid.GetCell(10, 10).MaterialIndex);
            Assert.NotEqual(water, grid.GetCell(11, 10).MaterialIndex);

            float maxTemp = 0;
            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    if (grid.GetCell(x, y).Temperature > maxTemp)
                        maxTemp = grid.GetCell(x, y).Temperature;
                }
            }
            Assert.True(maxTemp > 400.0f, $"Liquid NaK alloy + water detonation should deposit intense heat. Found {maxTemp}°C");
        }

        [Fact]
        public void HighTemperatureFires_RegistryAndProperties()
        {
            var registry = MaterialRegistry.CreateDefault();

            string[] fireIds = { "fire", "blue_fire", "green_fire", "white_fire", "plasma_fire" };
            float[] expectedTemps = { 800.0f, 1500.0f, 2000.0f, 2600.0f, 3500.0f };

            for (int i = 0; i < fireIds.Length; i++)
            {
                var mat = registry.TryGetMaterial(fireIds[i]);
                Assert.NotNull(mat);
                Assert.Equal(StateOfMatter.Energy, mat.Definition.State);
                Assert.Equal(expectedTemps[i], mat.Definition.DefaultTemperature);
                Assert.True(mat.Definition.HeatGenerated > 0, $"{fireIds[i]} should generate heat.");
                Assert.True(mat.Definition.Lifetime > 0, $"{fireIds[i]} should have ephemeral lifetime.");
                Assert.Equal("Flammables", mat.Definition.Category);
            }
        }

        [Fact]
        public void TorchEmitters_RegistryAndProperties()
        {
            var registry = MaterialRegistry.CreateDefault();

            (string torchId, string fireId, float temp)[] torches = {
                ("torch", "fire", 800.0f),
                ("blue_torch", "blue_fire", 1500.0f),
                ("green_torch", "green_fire", 2000.0f),
                ("white_torch", "white_fire", 2600.0f),
                ("plasma_torch", "plasma_fire", 3500.0f)
            };

            foreach (var (torchId, fireId, temp) in torches)
            {
                var torchMat = registry.TryGetMaterial(torchId);
                Assert.NotNull(torchMat);
                Assert.True(torchMat.Definition.IsEmitter, $"{torchId} should be an emitter.");
                Assert.True(torchMat.Definition.FixedTemperature, $"{torchId} should have fixed temperature.");
                Assert.Equal(temp, torchMat.Definition.DefaultTemperature);
                Assert.Equal("Tools", torchMat.Definition.Category);

                var fireMat = registry.TryGetMaterial(fireId);
                Assert.NotNull(fireMat);
                Assert.Equal(fireMat.Index, torchMat.EmitsMaterialIndex);
            }
        }

        [Fact]
        public void TorchEmitter_GeneratesFireParticles()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(15, 15, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort torchIdx = registry.GetIndex("torch");
            ushort fireIdx = registry.GetIndex("fire");

            // Place torch at (7, 10)
            grid.SetCell(7, 10, torchIdx);

            int generatedFires = 0;
            for (int step = 0; step < 20; step++)
            {
                engine.Step();
                for (int i = 0; i < grid.Cells.Length; i++)
                {
                    if (grid.Cells[i].MaterialIndex == fireIdx)
                    {
                        generatedFires++;
                    }
                }
            }

            Assert.True(generatedFires > 0, "Torch should continuously emit fire particles into surrounding cells.");
        }

        [Fact]
        public void BlueAndPlasmaTorches_GenerateRespectiveHighTempFires()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 123);
            var engine = new SimulationEngine(grid);

            ushort blueTorchIdx = registry.GetIndex("blue_torch");
            ushort blueFireIdx = registry.GetIndex("blue_fire");
            ushort plasmaTorchIdx = registry.GetIndex("plasma_torch");
            ushort plasmaFireIdx = registry.GetIndex("plasma_fire");

            grid.SetCell(5, 15, blueTorchIdx);
            grid.SetCell(15, 15, plasmaTorchIdx);

            int blueFires = 0;
            int plasmaFires = 0;

            for (int step = 0; step < 25; step++)
            {
                engine.Step();
                for (int i = 0; i < grid.Cells.Length; i++)
                {
                    if (grid.Cells[i].MaterialIndex == blueFireIdx) blueFires++;
                    if (grid.Cells[i].MaterialIndex == plasmaFireIdx) plasmaFires++;
                }
            }

            Assert.True(blueFires > 0, "Blue torch should emit blue fire particles.");
            Assert.True(plasmaFires > 0, "Plasma torch should emit plasma fire particles.");
        }

        [Fact]
        public void HighTemperatureFire_RadiatesMoreThermalHeatThanNormalFire()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 10, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort fireIdx = registry.GetIndex("fire");
            ushort plasmaFireIdx = registry.GetIndex("plasma_fire");
            ushort copperIdx = registry.GetIndex("copper");

            // Left side: Standard Fire directly underneath copper block
            grid.SetCell(4, 5, fireIdx);
            grid.SetCell(4, 4, copperIdx);

            // Right side: Plasma Fire directly underneath copper block
            grid.SetCell(14, 5, plasmaFireIdx);
            grid.SetCell(14, 4, copperIdx);

            engine.Step();

            float leftCopperTemp = grid.GetCell(4, 4).Temperature;
            float rightCopperTemp = grid.GetCell(14, 4).Temperature;

            Assert.True(rightCopperTemp > leftCopperTemp,
                $"Plasma fire should deposit significantly more thermal heat ({rightCopperTemp}°C) than standard fire ({leftCopperTemp}°C).");
        }

        [Fact]
        public void TorchAndFire_AliasesResolveCorrectly()
        {
            var registry = MaterialRegistry.CreateDefault();

            Assert.Equal(registry.GetIndex("torch"), registry.GetIndex("fire_spout"));
            Assert.Equal(registry.GetIndex("torch"), registry.GetIndex("fire_torch"));
            Assert.Equal(registry.GetIndex("blue_torch"), registry.GetIndex("blue_spout"));
            Assert.Equal(registry.GetIndex("blue_torch"), registry.GetIndex("blue_fire_spout"));
            Assert.Equal(registry.GetIndex("green_torch"), registry.GetIndex("green_spout"));
            Assert.Equal(registry.GetIndex("white_torch"), registry.GetIndex("white_spout"));
            Assert.Equal(registry.GetIndex("plasma_torch"), registry.GetIndex("plasma_spout"));

            Assert.Equal(registry.GetIndex("blue_fire"), registry.GetIndex("blue_flame"));
            Assert.Equal(registry.GetIndex("green_fire"), registry.GetIndex("chemical_fire"));
            Assert.Equal(registry.GetIndex("white_fire"), registry.GetIndex("solar_fire"));
            Assert.Equal(registry.GetIndex("plasma_fire"), registry.GetIndex("plasma_flame"));
        }

        [Fact]
        public void Thermodynamics_LavaOnStone_ConductsHeatThroughoutEntireRock()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(30, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort stoneIdx = registry.GetIndex("stone");
            ushort lavaIdx = registry.GetIndex("lava");

            // Create a contiguous stone rock slab from x=3 to x=20 at y=15, 16 with left rim at x=3, y=14
            for (int x = 3; x <= 20; x++)
            {
                grid.SetCell(x, 15, stoneIdx, 20.0f);
                grid.SetCell(x, 16, stoneIdx, 20.0f);
            }
            grid.SetCell(3, 14, stoneIdx, 20.0f);

            // Verify rock is initialized at ambient room temperature
            for (int x = 3; x <= 20; x++)
            {
                Assert.Equal(20.0f, grid.GetCell(x, 15).Temperature);
            }

            // Drop a small pool of lava on the left side of the stone rock (x=4..6, y=14)
            for (int x = 4; x <= 6; x++)
            {
                grid.SetCell(x, 14, lavaIdx, 1900.0f);
            }

            // Step the simulation
            for (int i = 0; i < 40; i++)
            {
                engine.Step();
            }

            // Verify that heat conducts away from the lava throughout the entire rock
            float tempContact = grid.GetCell(5, 15).Temperature;
            float tempNear = grid.GetCell(8, 15).Temperature;
            float tempMid = grid.GetCell(12, 15).Temperature;
            float tempFar = grid.GetCell(16, 15).Temperature;

            Assert.True(tempContact > 80.0f, $"Contact area should be heated by lava. Got {tempContact}°C");
            Assert.True(tempNear > 30.0f, $"Stone near lava should conduct heat away. Got {tempNear}°C");
            Assert.True(tempMid > 21.0f, $"Middle stone should conduct heat throughout rock. Got {tempMid}°C");
            Assert.True(tempFar >= 20.0f, $"Far stone should receive conducted heat instead of staying at ambient. Got {tempFar}°C");

            // Verify a smooth thermal gradient from contact to far end
            Assert.True(tempContact >= tempNear, "Temperature should decrease with distance from heat source.");
            Assert.True(tempNear >= tempMid, "Temperature should decrease with distance from heat source.");
            Assert.True(tempMid >= tempFar, "Temperature should decrease with distance from heat source.");
        }

        [Fact]
        public void Thermodynamics_LavaOnRockWithAttachedMaterial_ConductsHeatToAttachedStructure()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(30, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort stoneIdx = registry.GetIndex("stone");
            ushort ironIdx = registry.GetIndex("iron");
            ushort lavaIdx = registry.GetIndex("lava");

            // Create a stone rock from x=3 to x=8 with left rim, connected to an attached iron structure from x=9 to x=15
            grid.SetCell(3, 14, stoneIdx, 20.0f);
            for (int x = 3; x <= 8; x++)
            {
                grid.SetCell(x, 15, stoneIdx, 20.0f);
            }
            for (int x = 9; x <= 15; x++)
            {
                grid.SetCell(x, 15, ironIdx, 20.0f);
            }

            // Drop lava on the left side of the stone rock (x=4..6, y=14)
            for (int x = 4; x <= 6; x++)
            {
                grid.SetCell(x, 14, lavaIdx, 1900.0f);
            }

            // Step the simulation
            for (int i = 0; i < 30; i++)
            {
                engine.Step();
            }

            // Attached iron should heat up as heat conducts through the stone into the attached metal
            float ironAttachPoint = grid.GetCell(9, 15).Temperature;
            float ironFar = grid.GetCell(12, 15).Temperature;

            Assert.True(ironAttachPoint > 25.0f, $"Attached iron at junction should warm up from stone conduction. Got {ironAttachPoint}°C");
            Assert.True(ironFar > 20.5f, $"Heat should conduct across attached structure. Got {ironFar}°C");
        }

        [Fact]
        public void Thermodynamics_WaterAboveRock_ProtectsFromTorchUnderneath()
        {
            var registry = MaterialRegistry.CreateDefault();
            ushort stoneIdx = registry.GetIndex("stone");
            ushort lavaIdx = registry.GetIndex("lava");
            ushort torchIdx = registry.GetIndex("torch");
            ushort waterSpoutIdx = registry.GetIndex("water_spout");

            // Setup 1: Rock line with torch underneath and water spout above
            var gridCooled = new SandGrid(30, 30, registry, seed: 42);
            var engineCooled = new SimulationEngine(gridCooled);

            // Setup 2: Uncooled rock line with torch underneath
            var gridUncooled = new SandGrid(30, 30, registry, seed: 42);
            var engineUncooled = new SimulationEngine(gridUncooled);

            // Build horizontal line of rock (stone) at y=15, from x=5 to x=25
            for (int x = 5; x <= 25; x++)
            {
                gridCooled.SetCell(x, 15, stoneIdx, 20.0f);
                gridUncooled.SetCell(x, 15, stoneIdx, 20.0f);
            }

            // Torch placed underneath at (15, 17)
            gridCooled.SetCell(15, 17, torchIdx);
            gridUncooled.SetCell(15, 17, torchIdx);

            // Water spout placed above rock on cooled grid at (15, 8)
            gridCooled.SetCell(15, 8, waterSpoutIdx);

            // Run simulations for 100 ticks
            for (int i = 0; i < 100; i++)
            {
                engineCooled.Step();
                engineUncooled.Step();
            }

            // The water-cooled rock line should remain intact (rock did not melt or disintegrate)
            int cooledStoneCount = 0;
            string status = "";
            for (int x = 5; x <= 25; x++)
            {
                var mat = registry.GetMaterial(gridCooled.GetCell(x, 15).MaterialIndex);
                status += $"[{x}:{mat.Definition.Id}:{gridCooled.GetCell(x, 15).Temperature:F0}] ";
                if (gridCooled.GetCell(x, 15).MaterialIndex == stoneIdx)
                {
                    cooledStoneCount++;
                }
            }
            Assert.True(cooledStoneCount == 21, $"Expected 21 stones, got {cooledStoneCount}. Details: {status}");

            // The rock cell directly above the torch flame should be kept well below stone melting point (750°C)
            float cooledRockTemp = gridCooled.GetCell(15, 15).Temperature;
            Assert.True(cooledRockTemp < 700.0f, $"Water cooling should keep rock well below melting point (750°C). Got: {cooledRockTemp}°C");
        }

        [Fact]
        public void Thermodynamics_WaterAboveMetal_ProtectsFromMelting()
        {
            var registry = MaterialRegistry.CreateDefault();
            ushort copperIdx = registry.GetIndex("copper");
            ushort moltenCopperIdx = registry.GetIndex("molten_copper");
            ushort blueTorchIdx = registry.GetIndex("blue_torch");
            ushort waterSpoutIdx = registry.GetIndex("water_spout");

            // Setup 1: Water-cooled metal plate
            var gridCooled = new SandGrid(30, 30, registry, seed: 42);
            var engineCooled = new SimulationEngine(gridCooled);

            // Setup 2: Uncooled metal plate
            var gridUncooled = new SandGrid(30, 30, registry, seed: 42);
            var engineUncooled = new SimulationEngine(gridUncooled);

            // Build horizontal line of copper (melting point 1085°C) at y=16, from x=7 to x=23 with side retaining walls
            gridCooled.SetCell(6, 15, registry.GetIndex("wall"));
            gridCooled.SetCell(6, 16, registry.GetIndex("wall"));
            gridCooled.SetCell(24, 15, registry.GetIndex("wall"));
            gridCooled.SetCell(24, 16, registry.GetIndex("wall"));
            gridUncooled.SetCell(6, 15, registry.GetIndex("wall"));
            gridUncooled.SetCell(6, 16, registry.GetIndex("wall"));
            gridUncooled.SetCell(24, 15, registry.GetIndex("wall"));
            gridUncooled.SetCell(24, 16, registry.GetIndex("wall"));
            for (int x = 7; x <= 23; x++)
            {
                gridCooled.SetCell(x, 16, copperIdx, 20.0f);
                gridUncooled.SetCell(x, 16, copperIdx, 20.0f);
            }

            // Pre-fill a layer of water above the metal on cooled grid
            ushort waterIdx = registry.GetIndex("water");
            for (int x = 7; x <= 23; x++)
            {
                gridCooled.SetCell(x, 15, waterIdx, 20.0f);
            }

            // Blue Torch (1500°C) placed underneath at (15, 17)
            gridCooled.SetCell(15, 17, blueTorchIdx);
            gridUncooled.SetCell(15, 17, blueTorchIdx);

            // Water spouts above to cool the plate on cooled grid
            gridCooled.SetCell(10, 9, waterSpoutIdx);
            gridCooled.SetCell(15, 9, waterSpoutIdx);
            gridCooled.SetCell(20, 9, waterSpoutIdx);

            // Run simulations for 100 ticks
            for (int i = 0; i < 100; i++)
            {
                engineCooled.Step();
                engineUncooled.Step();
            }

            // Uncooled copper under the torch heats above 1085°C, melts and breaches the barrier at (15, 16)
            Assert.NotEqual(copperIdx, gridUncooled.GetCell(15, 16).MaterialIndex);

            // Cooled copper remains intact as solid metal across all 17 cells protected by water
            int solidCopperCount = 0;
            for (int x = 7; x <= 23; x++)
            {
                if (gridCooled.GetCell(x, 16).MaterialIndex == copperIdx)
                {
                    solidCopperCount++;
                }
            }
            Assert.Equal(17, solidCopperCount);

            // The copper temperature at the contact point is kept cool by the water on top
            float cooledCopperTemp = gridCooled.GetCell(15, 16).Temperature;
            Assert.True(cooledCopperTemp < 950.0f, $"Water cooling should keep copper well below melting point (1085°C). Got: {cooledCopperTemp}°C");
        }

        [Fact]
        public void Thermodynamics_BoilingWater_ExtractsLatentHeatFromHotNeighbor()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort stoneIdx = registry.GetIndex("stone");
            ushort waterIdx = registry.GetIndex("water");

            // Hot stone at 300°C next to water at 100°C
            grid.SetCell(10, 10, stoneIdx, 300.0f);
            grid.SetCell(10, 9, waterIdx, 100.0f);

            // Step engine
            engine.Step();

            // Water should have boiled to steam (which rises upwards)
            ushort steamIdx = registry.GetIndex("steam");
            Assert.True(grid.GetCell(10, 9).MaterialIndex == steamIdx || grid.GetCell(10, 8).MaterialIndex == steamIdx, "Water should boil into steam.");

            // Hot stone should have experienced latent heat extraction, dropping significantly
            float stoneTemp = grid.GetCell(10, 10).Temperature;
            Assert.True(stoneTemp < 280.0f, $"Boiling water should extract latent heat from hot stone. Actual: {stoneTemp}°C");
        }

        [Fact]
        public void Torches_GreenTorch2000Degrees_EasilyMeltsIronToItsMeltingPoint()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort ironIdx = registry.GetIndex("iron");
            ushort moltenIronIdx = registry.GetIndex("molten_iron");
            ushort greenTorchIdx = registry.GetIndex("green_torch");

            // Place iron block at (10, 10)
            grid.SetCell(10, 10, ironIdx, 20.0f);

            // Place 2000°C Green Torch directly underneath iron at (10, 11)
            grid.SetCell(10, 11, greenTorchIdx);

            // Step engine
            bool melted = false;
            for (int i = 0; i < 30; i++)
            {
                engine.Step();
                for (int y = 0; y < 20; y++)
                {
                    for (int x = 0; x < 20; x++)
                    {
                        if (grid.GetCell(x, y).MaterialIndex == moltenIronIdx)
                        {
                            melted = true;
                            break;
                        }
                    }
                    if (melted) break;
                }
            }

            Assert.True(melted, "A 2000°C Green Torch should easily heat and melt iron (1538°C) into molten iron.");
        }

        [Fact]
        public void Torches_WhiteTorch2600Degrees_FlameMeltsIronAndRock()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort ironIdx = registry.GetIndex("iron");
            ushort moltenIronIdx = registry.GetIndex("molten_iron");
            ushort stoneIdx = registry.GetIndex("stone");
            ushort lavaIdx = registry.GetIndex("lava");
            ushort whiteTorchIdx = registry.GetIndex("white_torch");

            // Place iron block at (8, 10) and stone block at (12, 10)
            grid.SetCell(8, 10, ironIdx, 20.0f);
            grid.SetCell(12, 10, stoneIdx, 20.0f);

            // Place 2600°C White Torch emitters below them
            grid.SetCell(8, 12, whiteTorchIdx);
            grid.SetCell(12, 12, whiteTorchIdx);

            // Step engine
            bool ironMelted = false;
            bool stoneMelted = false;
            for (int i = 0; i < 40; i++)
            {
                engine.Step();
                for (int y = 0; y < 20; y++)
                {
                    for (int x = 0; x < 20; x++)
                    {
                        if (grid.GetCell(x, y).MaterialIndex == moltenIronIdx) ironMelted = true;
                        if (grid.GetCell(x, y).MaterialIndex == lavaIdx) stoneMelted = true;
                    }
                }
            }

            Assert.True(ironMelted, "Flame from 2600°C White Torch should melt iron (1538°C) into molten iron.");
            Assert.True(stoneMelted, "Flame from 2600°C White Torch should melt stone (750°C) into lava.");
        }

        [Fact]
        public void Flames_WhiteFire2600Degrees_DirectContactMeltsIronQuickly()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(15, 15, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort ironIdx = registry.GetIndex("iron");
            ushort moltenIronIdx = registry.GetIndex("molten_iron");
            ushort whiteFireIdx = registry.GetIndex("white_fire");

            // Place iron at (7, 7)
            grid.SetCell(7, 7, ironIdx, 20.0f);
            // Place 2600°C white fire column below iron
            grid.SetCell(7, 8, whiteFireIdx, 2600.0f);
            grid.SetCell(7, 9, whiteFireIdx, 2600.0f);
            grid.SetCell(6, 8, whiteFireIdx, 2600.0f);
            grid.SetCell(8, 8, whiteFireIdx, 2600.0f);

            for (int i = 0; i < 20; i++)
            {
                engine.Step();
            }

            bool melted = false;
            for (int y = 0; y < 15; y++)
            {
                for (int x = 0; x < 15; x++)
                {
                    if (grid.GetCell(x, y).MaterialIndex == moltenIronIdx)
                    {
                        melted = true;
                        break;
                    }
                }
                if (melted) break;
            }

            Assert.True(melted, "Direct contact with 2600°C white fire should quickly heat iron past 1538°C and melt it.");
        }

        [Fact]
        public void Steam_Definition_HasCalibratedCondensationAndConductivity()
        {
            var registry = MaterialRegistry.CreateDefault();
            var steamMat = registry.GetMaterial(registry.GetIndex("steam"));

            Assert.NotNull(steamMat);
            Assert.Equal(StateOfMatter.Gas, steamMat.Definition.State);
            Assert.Equal(40.0f, steamMat.Definition.CondensationPoint);
            Assert.Equal(0.08f, steamMat.Definition.ThermalConductivity);
            Assert.Equal(400, steamMat.Definition.Lifetime);
            Assert.Equal("water", steamMat.Definition.CondenseTarget);
        }

        [Fact]
        public void Steam_StaysInGasPhaseLonger_RisesAndDoesNotImmediatelyCondense()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort steamIdx = registry.GetIndex("steam");
            ushort waterIdx = registry.GetIndex("water");

            // Place a cluster of steam particles near the bottom at 110°C
            for (int x = 8; x <= 12; x++)
            {
                grid.SetCell(x, 15, steamIdx, 110.0f);
            }

            // Step for 25 simulation ticks
            for (int i = 0; i < 25; i++)
            {
                engine.Step();
            }

            // Count steam and water particles
            int steamCount = 0;
            int waterCount = 0;
            int topHalfSteamCount = 0;

            for (int y = 0; y < 20; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    if (grid.GetCell(x, y).MaterialIndex == steamIdx)
                    {
                        steamCount++;
                        if (y < 10) topHalfSteamCount++;
                    }
                    else if (grid.GetCell(x, y).MaterialIndex == waterIdx)
                    {
                        waterCount++;
                    }
                }
            }

            // Steam should still be in the gas phase and risen into the upper half of the grid
            Assert.True(steamCount >= 4, $"Steam should stay in gas phase longer. Remaining steam: {steamCount}");
            Assert.True(topHalfSteamCount >= 3, $"Steam should rise towards the ceiling in the gas phase. In top half: {topHalfSteamCount}");
            Assert.True(waterCount == 0, "Steam should not immediately condense into water in ambient air after 25 ticks.");
        }

        [Fact]
        public void Steam_BoiledFromWater_FloatsUpwardInGasPhaseAcrossMultipleTicks()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort waterIdx = registry.GetIndex("water");
            ushort steamIdx = registry.GetIndex("steam");
            ushort heaterIdx = registry.GetIndex("heater");

            // Place heater at (10, 18) and water at (10, 17)
            grid.SetCell(10, 18, heaterIdx);
            grid.SetCell(10, 17, waterIdx, 90.0f);

            // Step engine to boil water into steam
            for (int i = 0; i < 20; i++)
            {
                engine.Step();
            }

            // Verify steam was generated and has risen into gas phase without instantly condensing back
            bool hasSteamInUpperRegion = false;
            for (int y = 0; y < 14; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    if (grid.GetCell(x, y).MaterialIndex == steamIdx)
                    {
                        hasSteamInUpperRegion = true;
                        break;
                    }
                }
                if (hasSteamInUpperRegion) break;
            }

            Assert.True(hasSteamInUpperRegion, "Boiled water should form steam that rises and stays in the gas phase.");
        }

        [Fact]
        public void ApplyHeatGlow_WhenMaterialsGetHot_TintsRedNotYellow()
        {
            // Base gray stone color (0xFF808080: R=128, G=128, B=128)
            uint stoneBaseColor = 0xFF808080;

            // Ambient / cold temperature (20°C) -> unchanged
            uint ambientColor = SandGrid.ApplyHeatGlow(stoneBaseColor, 20.0f);
            Assert.Equal(stoneBaseColor, ambientColor);

            // Hot temperature (800°C) -> Red channel becomes dominant over Green and Blue
            uint hotColor = SandGrid.ApplyHeatGlow(stoneBaseColor, 800.0f);
            byte hotR = (byte)((hotColor >> 16) & 0xFF);
            byte hotG = (byte)((hotColor >> 8) & 0xFF);
            byte hotB = (byte)(hotColor & 0xFF);

            Assert.True(hotR > 180, $"Red channel should be high when hot. Got: {hotR}");
            Assert.True(hotR > hotG + 50, $"Red channel ({hotR}) should be significantly higher than Green ({hotG}), ensuring red rather than yellow.");
            Assert.True(hotR > hotB + 50, $"Red channel ({hotR}) should be significantly higher than Blue ({hotB}).");

            // Very hot temperature (1400°C) -> Intense glowing red
            uint extremeColor = SandGrid.ApplyHeatGlow(stoneBaseColor, 1400.0f);
            byte extR = (byte)((extremeColor >> 16) & 0xFF);
            byte extG = (byte)((extremeColor >> 8) & 0xFF);
            byte extB = (byte)(extremeColor & 0xFF);

            Assert.True(extR >= 250, $"Red channel should saturate near 255 at extreme temperatures. Got: {extR}");
            Assert.True(extG < 70, $"Green channel should remain low at extreme temperatures to avoid yellowing. Got: {extG}");
            Assert.True(extB < 50, $"Blue channel should remain low at extreme temperatures. Got: {extB}");
        }

        [Fact]
        public void RenderToPixelBuffer_WhenMaterialsHeated_RendersRedGlow()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(10, 10, registry);
            ushort stoneIdx = registry.GetIndex("stone");

            // Cell (2, 2) is cold stone (20°C), Cell (5, 5) is glowing hot stone (1200°C)
            grid.SetCell(2, 2, stoneIdx, 20.0f);
            grid.SetCell(5, 5, stoneIdx, 1200.0f);

            int[] pixelBuffer = new int[10 * 10];
            grid.RenderToPixelBuffer(pixelBuffer, ViewMode.Normal);

            uint coldPixel = (uint)pixelBuffer[2 * 10 + 2];
            uint hotPixel = (uint)pixelBuffer[5 * 10 + 5];

            byte coldR = (byte)((coldPixel >> 16) & 0xFF);
            byte coldG = (byte)((coldPixel >> 8) & 0xFF);
            byte coldB = (byte)(coldPixel & 0xFF);

            byte hotR = (byte)((hotPixel >> 16) & 0xFF);
            byte hotG = (byte)((hotPixel >> 8) & 0xFF);
            byte hotB = (byte)(hotPixel & 0xFF);

            // Cold stone has balanced R, G, B
            Assert.True(Math.Abs(coldR - coldG) < 20);

            // Hot stone has dominant red color (red >> green & red >> blue)
            Assert.True(hotR >= 240, $"Hot stone red channel should be glowing bright. Got: {hotR}");
            Assert.True(hotG < 80, $"Hot stone green channel should be suppressed for red glow. Got: {hotG}");
            Assert.True(hotR > hotG * 2, $"Hot stone should be distinctly red rather than yellow (R={hotR}, G={hotG}).");
        }

        [Fact]
        public void TorchesAndFlames_PreserveTheirIntrinsicColors_WhenRendered()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry);

            ushort blueFireIdx = registry.GetIndex("blue_fire");
            ushort greenFireIdx = registry.GetIndex("green_fire");
            ushort plasmaFireIdx = registry.GetIndex("plasma_fire");
            ushort whiteFireIdx = registry.GetIndex("white_fire");
            ushort blueTorchIdx = registry.GetIndex("blue_torch");
            ushort greenTorchIdx = registry.GetIndex("green_torch");
            ushort plasmaTorchIdx = registry.GetIndex("plasma_torch");
            ushort stoneIdx = registry.GetIndex("stone");
            ushort ironIdx = registry.GetIndex("iron");

            grid.SetCell(0, 0, blueFireIdx, 1500.0f);
            grid.SetCell(1, 0, greenFireIdx, 2000.0f);
            grid.SetCell(2, 0, plasmaFireIdx, 3500.0f);
            grid.SetCell(3, 0, whiteFireIdx, 2600.0f);
            grid.SetCell(4, 0, blueTorchIdx, 1500.0f);
            grid.SetCell(5, 0, greenTorchIdx, 2000.0f);
            grid.SetCell(6, 0, plasmaTorchIdx, 3500.0f);
            grid.SetCell(7, 0, stoneIdx, 1200.0f);
            grid.SetCell(8, 0, ironIdx, 1400.0f);

            int[] pixelBuffer = new int[20 * 20];
            grid.RenderToPixelBuffer(pixelBuffer, ViewMode.Normal);

            // Helper to get RGB
            (byte r, byte g, byte b) GetRGB(int x, int y)
            {
                uint p = (uint)pixelBuffer[y * 20 + x];
                return ((byte)((p >> 16) & 0xFF), (byte)((p >> 8) & 0xFF), (byte)(p & 0xFF));
            }

            // Blue Fire: should stay distinctly blue (B > R)
            var (bfR, bfG, bfB) = GetRGB(0, 0);
            Assert.True(bfB > bfR, $"Blue fire should retain blue color (R={bfR}, G={bfG}, B={bfB})");

            // Green Fire: should stay distinctly green (G > R)
            var (gfR, gfG, gfB) = GetRGB(1, 0);
            Assert.True(gfG > gfR, $"Green fire should retain green color (R={gfR}, G={gfG}, B={gfB})");

            // Plasma Fire: should retain purple/plasma hue (significant blue and red)
            var (pfR, pfG, pfB) = GetRGB(2, 0);
            Assert.True(pfB > 150 && pfR > 150, $"Plasma fire should retain purple/plasma hue (R={pfR}, G={pfG}, B={pfB})");

            // White Fire: should retain high brightness across R, G, B
            var (wfR, wfG, wfB) = GetRGB(3, 0);
            Assert.True(wfR > 200 && wfG > 200 && wfB > 200, $"White fire should retain bright white color (R={wfR}, G={wfG}, B={wfB})");

            // Blue Torch: should retain blue color (B > R)
            var (btR, btG, btB) = GetRGB(4, 0);
            Assert.True(btB > btR, $"Blue torch should retain blue color (R={btR}, G={btG}, B={btB})");

            // Green Torch: should retain green color (G > R)
            var (gtR, gtG, gtB) = GetRGB(5, 0);
            Assert.True(gtG > gtR, $"Green torch should retain green color (R={gtR}, G={gtG}, B={gtB})");

            // Plasma Torch: should retain purple color (B > 100)
            var (ptR, ptG, ptB) = GetRGB(6, 0);
            Assert.True(ptB > 100, $"Plasma torch should retain purple color (R={ptR}, G={ptG}, B={ptB})");

            // Stone at 1200°C: should glow bright red (R > G * 2 and R > B * 2)
            var (stR, stG, stB) = GetRGB(7, 0);
            Assert.True(stR >= 240 && stR > stG * 2, $"Hot stone should glow red (R={stR}, G={stG}, B={stB})");

            // Iron at 1400°C: should glow bright red (R > G * 2 and R > B * 2)
            var (irR, irG, irB) = GetRGB(8, 0);
            Assert.True(irR >= 240 && irR > irG * 2, $"Hot iron should glow red (R={irR}, G={irG}, B={irB})");
        }

        [Fact]
        public void Fire_DoesNotInstantlyMeltRocksOrMetals_HeatsProgressively()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(15, 15, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort stoneIdx = registry.GetIndex("stone");
            ushort ironIdx = registry.GetIndex("iron");
            ushort fireIdx = registry.GetIndex("fire");
            ushort lavaIdx = registry.GetIndex("lava");
            ushort moltenIronIdx = registry.GetIndex("molten_iron");

            // Place stone at (5, 5) and iron at (10, 5)
            grid.SetCell(5, 5, stoneIdx, 20.0f);
            grid.SetCell(10, 5, ironIdx, 20.0f);

            // Place fire beneath both
            grid.SetCell(5, 6, fireIdx, 800.0f);
            grid.SetCell(10, 6, fireIdx, 800.0f);

            // Step for 5 ticks
            for (int i = 0; i < 5; i++)
            {
                engine.Step();
            }

            // Stone and iron should still be in their solid states, not instantly melted
            Assert.Equal(stoneIdx, grid.GetCell(5, 5).MaterialIndex);
            Assert.Equal(ironIdx, grid.GetCell(10, 5).MaterialIndex);

            // They should have warmed up progressively rather than jumping to 800°C immediately
            float stoneTemp = grid.GetCell(5, 5).Temperature;
            float ironTemp = grid.GetCell(10, 5).Temperature;

            Assert.True(stoneTemp > 30.0f && stoneTemp < 700.0f, $"Stone should warm progressively. Got: {stoneTemp}°C");
            Assert.True(ironTemp > 30.0f && ironTemp < 700.0f, $"Iron should warm progressively. Got: {ironTemp}°C");
        }

        [Fact]
        public void HeatDissipation_HotObjectsAndAir_DissipateHeatToAmbientEnvironment()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort stoneIdx = registry.GetIndex("stone");
            ushort ironIdx = registry.GetIndex("iron");

            // Place hot stone and hot iron blocks surrounded by ambient air (20°C)
            grid.SetCell(5, 5, stoneIdx, 600.0f);
            grid.SetCell(15, 5, ironIdx, 600.0f);
            grid.GetCell(10, 10).Temperature = 500.0f; // Hot air pocket

            // Step for 80 ticks
            for (int i = 0; i < 80; i++)
            {
                engine.Step();
            }

            float finalStoneTemp = grid.GetCell(5, 5).Temperature;
            float finalIronTemp = grid.GetCell(15, 5).Temperature;
            float finalAirTemp = grid.GetCell(10, 10).Temperature;

            Assert.True(finalStoneTemp < 250.0f, $"Stone should dissipate heat to ambient over time. Got: {finalStoneTemp}°C");
            Assert.True(finalIronTemp < 250.0f, $"Iron should dissipate heat to ambient over time. Got: {finalIronTemp}°C");
            Assert.True(finalAirTemp < 60.0f, $"Air should dissipate heat to ambient quickly. Got: {finalAirTemp}°C");
        }

        [Fact]
        public void Steam_TouchingOrSurroundingCopper_DoesNotMeltCopper()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort copperIdx = registry.GetIndex("copper");
            ushort moltenCopperIdx = registry.GetIndex("molten_copper");
            ushort steamIdx = registry.GetIndex("steam");

            // Place copper block at center
            for (int x = 8; x <= 12; x++)
            {
                for (int y = 8; y <= 10; y++)
                {
                    grid.SetCell(x, y, copperIdx, 20.0f);
                }
            }

            // Surround copper block with hot steam at 150°C
            for (int x = 7; x <= 13; x++)
            {
                grid.SetCell(x, 7, steamIdx, 150.0f);
                grid.SetCell(x, 11, steamIdx, 150.0f);
            }
            for (int y = 8; y <= 10; y++)
            {
                grid.SetCell(7, y, steamIdx, 150.0f);
                grid.SetCell(13, y, steamIdx, 150.0f);
            }

            // Step engine for 50 ticks
            for (int i = 0; i < 50; i++)
            {
                engine.Step();
                var c = grid.GetCell(8, 8);
                if (c.MaterialIndex != copperIdx)
                {
                    Assert.Fail($"At tick {i}, copper at (8,8) became Mat={c.MaterialIndex}, Temp={c.Temperature}");
                }
            }

            // Verify all copper cells remain solid copper, none melted
            int copperCount = 0;
            for (int x = 8; x <= 12; x++)
            {
                for (int y = 8; y <= 10; y++)
                {
                    var cell = grid.GetCell(x, y);
                    Assert.Equal(copperIdx, cell.MaterialIndex);
                    Assert.NotEqual(moltenCopperIdx, cell.MaterialIndex);
                    Assert.True(cell.Temperature < 200.0f, $"Copper should remain well below melting point (1085°C). Got: {cell.Temperature}°C");
                    copperCount++;
                }
            }
            Assert.Equal(15, copperCount);
        }

        [Fact]
        public void BoilingWater_GeneratingSteamOnCopper_CoolsCopperAndDoesNotMelt()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort copperIdx = registry.GetIndex("copper");
            ushort moltenCopperIdx = registry.GetIndex("molten_copper");
            ushort waterIdx = registry.GetIndex("water");

            // Hot copper plate at 400°C
            for (int x = 5; x <= 15; x++)
            {
                grid.SetCell(x, 12, copperIdx, 400.0f);
            }

            // Water poured on top
            for (int x = 6; x <= 14; x++)
            {
                grid.SetCell(x, 11, waterIdx, 20.0f);
            }

            // Step engine for 60 ticks
            for (int i = 0; i < 60; i++)
            {
                engine.Step();
            }

            // Verify copper stayed solid copper, cooled down, and never melted
            for (int x = 5; x <= 15; x++)
            {
                var cell = grid.GetCell(x, 12);
                Assert.Equal(copperIdx, cell.MaterialIndex);
                Assert.NotEqual(moltenCopperIdx, cell.MaterialIndex);
                Assert.True(cell.Temperature < 350.0f, $"Water cooling should reduce copper temperature. Got: {cell.Temperature}°C");
            }
        }

        [Fact]
        public void Diagnostic_Steam_Copper_Interaction()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(200, 150, registry, seed: 42);
            int w = grid.Width;
            int h = grid.Height;
            ushort wallIdx = registry.GetIndex("wall");
            ushort stoneIdx = registry.GetIndex("stone");
            ushort copperIdx = registry.GetIndex("copper");
            ushort torchIdx = registry.GetIndex("torch");
            ushort blueTorchIdx = registry.GetIndex("blue_torch");
            ushort waterSpoutIdx = registry.GetIndex("water_spout");
            ushort drainIdx = registry.GetIndex("drain");
            ushort moltenCopperIdx = registry.GetIndex("molten_copper");

            // Right Side: WATER-COOLED Rock & Metal
            int rightMidX = 3 * w / 4;
            grid.DrawBox(rightMidX - 35, h - 55, rightMidX + 35, h - 53, stoneIdx, filled: true);
            grid.SetCell(rightMidX, h - 75, waterSpoutIdx);
            grid.DrawBox(rightMidX - 25, h - 35, rightMidX + 25, h - 33, copperIdx, filled: true);
            grid.SetCell(rightMidX, h - 48, waterSpoutIdx);
            grid.SetCell(rightMidX - 15, h - 20, torchIdx);
            grid.SetCell(rightMidX + 15, h - 20, blueTorchIdx);
            grid.SetCell(rightMidX, h - 42, torchIdx);

            var engine = new SimulationEngine(grid);

            for (int step = 0; step < 200; step++)
            {
                engine.Step();
                int rightMoltenCopper = 0;
                int rightCopper = 0;
                for (int y = 0; y < grid.Height; y++)
                {
                    for (int x = grid.Width / 2; x < grid.Width; x++)
                    {
                        var mat = grid.GetCell(x, y).MaterialIndex;
                        if (mat == moltenCopperIdx) rightMoltenCopper++;
                        if (mat == copperIdx) rightCopper++;
                    }
                }
                if (rightMoltenCopper > 0)
                {
                    System.Console.WriteLine($"[DEBUG_LOG] Step {step}: Water cooled side has molten copper! count={rightMoltenCopper}, remaining copper={rightCopper}");
                    for (int y = 0; y < grid.Height; y++)
                    {
                        for (int x = grid.Width / 2; x < grid.Width; x++)
                        {
                            if (grid.GetCell(x, y).MaterialIndex == moltenCopperIdx)
                            {
                                var cell = grid.GetCell(x, y);
                                System.Console.WriteLine($"[DEBUG_LOG] Molten copper at ({x},{y}), Temp={cell.Temperature}");
                                System.Console.WriteLine($"[DEBUG_LOG] Above: {grid.GetCell(x, y-1).MaterialIndex} temp={grid.GetCell(x, y-1).Temperature}");
                                System.Console.WriteLine($"[DEBUG_LOG] Below: {grid.GetCell(x, y+1).MaterialIndex} temp={grid.GetCell(x, y+1).Temperature}");
                                System.Console.WriteLine($"[DEBUG_LOG] Left: {grid.GetCell(x-1, y).MaterialIndex} temp={grid.GetCell(x-1, y).Temperature}");
                                System.Console.WriteLine($"[DEBUG_LOG] Right: {grid.GetCell(x+1, y).MaterialIndex} temp={grid.GetCell(x+1, y).Temperature}");
                            }
                        }
                    }
                    break;
                }
            }
        }

        [Fact]
        public void Thermodynamics_MoltenMetals_StayLiquidForRealisticDurationWhenPoured()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(30, 40, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort moltenIronIdx = registry.GetIndex("molten_iron");
            ushort moltenCopperIdx = registry.GetIndex("molten_copper");
            ushort moltenGoldIdx = registry.GetIndex("molten_gold");
            ushort moltenLeadIdx = registry.GetIndex("molten_lead");

            // Place drops of molten metals in air
            grid.SetCell(5, 5, moltenIronIdx);
            grid.SetCell(12, 5, moltenCopperIdx);
            grid.SetCell(18, 5, moltenGoldIdx);
            grid.SetCell(24, 5, moltenLeadIdx);

            // Step simulation for 20 frames
            for (int i = 0; i < 20; i++)
            {
                engine.Step();
            }

            // Verify each molten metal is still in liquid state after 20 frames of falling/flowing
            int moltenIronCount = 0;
            int moltenCopperCount = 0;
            int moltenGoldCount = 0;
            int moltenLeadCount = 0;

            for (int y = 0; y < 40; y++)
            {
                for (int x = 0; x < 30; x++)
                {
                    ushort mat = grid.GetCell(x, y).MaterialIndex;
                    if (mat == moltenIronIdx) moltenIronCount++;
                    if (mat == moltenCopperIdx) moltenCopperCount++;
                    if (mat == moltenGoldIdx) moltenGoldCount++;
                    if (mat == moltenLeadIdx) moltenLeadCount++;
                }
            }

            Assert.True(moltenIronCount > 0, "Molten iron should remain liquid after 20 frames in air.");
            Assert.True(moltenCopperCount > 0, "Molten copper should remain liquid after 20 frames in air.");
            Assert.True(moltenGoldCount > 0, "Molten gold should remain liquid after 20 frames in air.");
            Assert.True(moltenLeadCount > 0, "Molten lead should remain liquid after 20 frames in air.");
        }

        [Fact]
        public void Thermodynamics_MoltenMetalPool_RemainsMoltenForExtendedTime()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(25, 25, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort moltenIronIdx = registry.GetIndex("molten_iron");
            ushort wallIdx = registry.GetIndex("wall");

            // Build a container
            grid.DrawBox(8, 15, 16, 20, wallIdx, filled: false);

            // Fill container with molten iron pool
            for (int x = 9; x <= 15; x++)
            {
                for (int y = 16; y <= 19; y++)
                {
                    grid.SetCell(x, y, moltenIronIdx);
                }
            }

            // Step 60 frames
            for (int i = 0; i < 60; i++)
            {
                engine.Step();
            }

            // Core of the pool should still be molten iron
            int remainingMolten = 0;
            for (int x = 9; x <= 15; x++)
            {
                for (int y = 16; y <= 19; y++)
                {
                    if (grid.GetCell(x, y).MaterialIndex == moltenIronIdx) remainingMolten++;
                }
            }

            Assert.True(remainingMolten >= 15, $"Pool of molten iron should remain largely liquid after 60 frames. Remaining: {remainingMolten}/28");
        }

        [Fact]
        public void Thermodynamics_MoltenMetal_WaterQuenchesFasterThanAir()
        {
            var registry = MaterialRegistry.CreateDefault();
            
            // Grid 1: Molten metal quenched with water in a basin
            var gridWater = new SandGrid(20, 20, registry, seed: 42);
            var engineWater = new SimulationEngine(gridWater);

            // Grid 2: Molten metal cooling in air in a basin
            var gridAir = new SandGrid(20, 20, registry, seed: 42);
            var engineAir = new SimulationEngine(gridAir);

            ushort moltenIronIdx = registry.GetIndex("molten_iron");
            ushort waterIdx = registry.GetIndex("water");
            ushort wallIdx = registry.GetIndex("wall");

            // Build closed containers with full walls and floors
            gridWater.DrawBox(8, 12, 12, 16, wallIdx, filled: false);
            gridWater.DrawBox(9, 15, 11, 15, moltenIronIdx, 1750.0f);
            gridWater.DrawBox(9, 14, 11, 14, waterIdx, 20.0f);

            gridAir.DrawBox(8, 12, 12, 16, wallIdx, filled: false);
            gridAir.DrawBox(9, 15, 11, 15, moltenIronIdx, 1750.0f);

            // Step both for 25 frames
            for (int i = 0; i < 25; i++)
            {
                engineWater.Step();
                engineAir.Step();
            }

            float tempWater = gridWater.GetCell(10, 15).Temperature;
            float tempAir = gridAir.GetCell(10, 15).Temperature;

            Assert.True(tempWater < tempAir - 100.0f, $"Water quenching should cool molten iron much faster than air. Water={tempWater}°C vs Air={tempAir}°C");
        }

        [Fact]
        public void Sand_FallsThroughTorch_LeavesTorchIntactAndEmitsFlame()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort torchIdx = registry.GetIndex("torch");
            ushort sandIdx = registry.GetIndex("sand");
            ushort fireIdx = registry.GetIndex("fire");

            // Place torch at (10, 10)
            grid.SetCell(10, 10, torchIdx);
            // Place sand right above torch at (10, 9)
            grid.SetCell(10, 9, sandIdx);

            // Step once: sand should pass through torch into cell (10, 11)
            engine.Step();

            Assert.Equal(torchIdx, grid.GetCell(10, 10).MaterialIndex);
            Assert.Equal(sandIdx, grid.GetCell(10, 11).MaterialIndex);

            // Run several more steps: torch should continue emitting flame above and around it
            int fireCount = 0;
            for (int i = 0; i < 15; i++)
            {
                engine.Step();
                for (int y = 0; y < 20; y++)
                {
                    for (int x = 0; x < 20; x++)
                    {
                        if (grid.GetCell(x, y).MaterialIndex == fireIdx) fireCount++;
                    }
                }
            }

            Assert.True(fireCount > 0, "Torch must continue emitting flame after sand falls through.");
        }

        [Fact]
        public void ContinuousSandStream_FallsThroughTorchWithoutSmothering()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 30, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort torchIdx = registry.GetIndex("torch");
            ushort sandIdx = registry.GetIndex("sand");
            ushort fireIdx = registry.GetIndex("fire");

            // Place torch at (10, 15)
            grid.SetCell(10, 15, torchIdx);

            int totalFiresObserved = 0;

            // Continually drop sand above torch over 30 steps
            for (int step = 0; step < 30; step++)
            {
                // Drop a new sand particle from top
                if (grid.GetCell(10, 5).IsEmpty)
                {
                    grid.SetCell(10, 5, sandIdx);
                }

                engine.Step();

                // Count fire emissions
                for (int y = 0; y < 30; y++)
                {
                    for (int x = 0; x < 20; x++)
                    {
                        if (grid.GetCell(x, y).MaterialIndex == fireIdx)
                        {
                            totalFiresObserved++;
                        }
                    }
                }
            }

            // Verify sand reached below torch (y > 15)
            int sandBelowTorch = 0;
            for (int y = 16; y < 30; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    if (grid.GetCell(x, y).MaterialIndex == sandIdx) sandBelowTorch++;
                }
            }

            Assert.True(sandBelowTorch >= 5, $"Sand should fall through torch to underneath. Sand below torch: {sandBelowTorch}");
            Assert.True(totalFiresObserved > 0, $"Torch should not be smothered and should emit fire. Fires observed: {totalFiresObserved}");
            // Sand directly on top of the torch (10, 14) should not remain permanently stuck/bunched
            Assert.Equal(torchIdx, grid.GetCell(10, 15).MaterialIndex);
        }

        [Fact]
        public void Powders_FallThroughVariousTorchTypes()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(25, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort blueTorch = registry.GetIndex("blue_torch");
            ushort greenTorch = registry.GetIndex("green_torch");
            ushort whiteTorch = registry.GetIndex("white_torch");
            ushort plasmaTorch = registry.GetIndex("plasma_torch");

            ushort gunpowder = registry.GetIndex("gunpowder");
            ushort ash = registry.GetIndex("ash");
            ushort sawdust = registry.GetIndex("sawdust");
            ushort sand = registry.GetIndex("sand");

            grid.SetCell(4, 10, blueTorch);
            grid.SetCell(4, 9, gunpowder);

            grid.SetCell(10, 10, greenTorch);
            grid.SetCell(10, 9, ash);

            grid.SetCell(16, 10, whiteTorch);
            grid.SetCell(16, 9, sawdust);

            grid.SetCell(22, 10, plasmaTorch);
            grid.SetCell(22, 9, sand);

            // Step 1: All powders should pass through their respective torches into y=11
            engine.Step();

            Assert.Equal(blueTorch, grid.GetCell(4, 10).MaterialIndex);
            Assert.Equal(greenTorch, grid.GetCell(10, 10).MaterialIndex);
            Assert.Equal(whiteTorch, grid.GetCell(16, 10).MaterialIndex);
            Assert.Equal(plasmaTorch, grid.GetCell(22, 10).MaterialIndex);

            Assert.Equal(gunpowder, grid.GetCell(4, 11).MaterialIndex);
            Assert.Equal(ash, grid.GetCell(10, 11).MaterialIndex);
            Assert.Equal(sawdust, grid.GetCell(16, 11).MaterialIndex);
            Assert.Equal(sand, grid.GetCell(22, 11).MaterialIndex);
        }

        [Fact]
        public void Sand_FallsThroughFireEnergyParticles()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(15, 15, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort fireIdx = registry.GetIndex("fire");
            ushort sandIdx = registry.GetIndex("sand");

            // Place fire below sand
            grid.SetCell(7, 8, fireIdx);
            grid.SetCell(7, 7, sandIdx);

            engine.Step();

            // Sand should have fallen into or through y=8, not blocked at y=7
            Assert.Equal(sandIdx, grid.GetCell(7, 8).MaterialIndex);
        }

        [Fact]
        public void MoltenMetal_FallsThroughTorch_LeavesTorchIntact()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 20, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort torchIdx = registry.GetIndex("torch");
            ushort moltenLead = registry.GetIndex("molten_lead");

            // Place torch at (10, 10)
            grid.SetCell(10, 10, torchIdx);
            // Place molten lead directly above torch at (10, 9)
            grid.SetCell(10, 9, moltenLead, 400.0f);

            // Step once: molten lead should pass through torch into cell (10, 11)
            engine.Step();

            Assert.Equal(torchIdx, grid.GetCell(10, 10).MaterialIndex);
            Assert.Equal(moltenLead, grid.GetCell(10, 11).MaterialIndex);
        }

        [Fact]
        public void TorchMeltsOverheadLead_MoltenParticlesFallThroughTorchAndFlamesContinue()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 25, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort torchIdx = registry.GetIndex("torch");
            ushort leadIdx = registry.GetIndex("lead");
            ushort moltenLead = registry.GetIndex("molten_lead");
            ushort fireIdx = registry.GetIndex("fire");

            // Place torch at (10, 15)
            grid.SetCell(10, 15, torchIdx);

            // Place lead above torch at (10, 12), (10, 13)
            grid.SetCell(10, 12, leadIdx, 20.0f);
            grid.SetCell(10, 13, leadIdx, 20.0f);

            int totalFires = 0;
            // Run simulation steps
            for (int step = 0; step < 50; step++)
            {
                engine.Step();

                for (int y = 0; y < 25; y++)
                {
                    for (int x = 0; x < 20; x++)
                    {
                        if (grid.GetCell(x, y).MaterialIndex == fireIdx)
                        {
                            totalFires++;
                        }
                    }
                }
            }

            // Check if lead melted and fell through torch to y > 15
            int leadBelowTorch = 0;
            for (int y = 16; y < 25; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    var idx = grid.GetCell(x, y).MaterialIndex;
                    if (idx == moltenLead || idx == leadIdx)
                    {
                        leadBelowTorch++;
                    }
                }
            }

            Assert.True(leadBelowTorch >= 1, $"Melted lead particles should fall through torch into space below. Found: {leadBelowTorch}");
            Assert.True(totalFires > 0, $"Torch should continue to produce fire emissions while and after melting overhead lead. Fires: {totalFires}");
            Assert.Equal(torchIdx, grid.GetCell(10, 15).MaterialIndex);
        }

        [Fact]
        public void TorchMeltsOverheadIce_WaterFallsThroughTorch()
        {
            var registry = MaterialRegistry.CreateDefault();
            var grid = new SandGrid(20, 25, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort torchIdx = registry.GetIndex("torch");
            ushort iceIdx = registry.GetIndex("ice");
            ushort waterIdx = registry.GetIndex("water");

            // Place torch at (10, 15)
            grid.SetCell(10, 15, torchIdx);
            // Place ice at (10, 14)
            grid.SetCell(10, 14, iceIdx, -10.0f);

            for (int step = 0; step < 20; step++)
            {
                engine.Step();
            }

            // Verify water fell below torch
            int waterBelowTorch = 0;
            for (int y = 16; y < 25; y++)
            {
                for (int x = 0; x < 20; x++)
                {
                    var idx = grid.GetCell(x, y).MaterialIndex;
                    if (idx == waterIdx) waterBelowTorch++;
                }
            }

            Assert.True(waterBelowTorch >= 1, $"Melted water should fall through the torch to below. Found: {waterBelowTorch}");
            Assert.Equal(torchIdx, grid.GetCell(10, 15).MaterialIndex);
        }

        [Fact]
        public void NewExplosives_LoadedInRegistryWithExpectedProperties()
        {
            var registry = MaterialRegistry.CreateDefault();

            ushort infernoIdx = registry.GetIndex("inferno_bomb");
            Assert.True(infernoIdx > 0, "inferno_bomb should be registered.");
            var infernoMat = registry.GetMaterial(infernoIdx);
            Assert.True(infernoMat.Definition.IsExplosive, "inferno_bomb should be explosive.");
            Assert.Equal("Explosives", infernoMat.Definition.Category);
            Assert.Equal(100, infernoMat.Definition.ExplosionRadius);
            Assert.True(infernoMat.Definition.ExplosionFireCount >= 1000);
            Assert.Equal(StateOfMatter.Solid, infernoMat.Definition.State);

            // Test aliases
            Assert.Equal(infernoIdx, registry.GetIndex("inferno bomb"));
            Assert.Equal(infernoIdx, registry.GetIndex("inferno"));
            Assert.Equal(infernoIdx, registry.GetIndex("hellfire_bomb"));
            Assert.Equal(infernoIdx, registry.GetIndex("hellfire"));
            Assert.Equal(infernoIdx, registry.GetIndex("firestorm_bomb"));

            ushort supernovaIdx = registry.GetIndex("supernova_charge");
            Assert.True(supernovaIdx > 0, "supernova_charge should be registered.");
            var supernovaMat = registry.GetMaterial(supernovaIdx);
            Assert.True(supernovaMat.Definition.IsExplosive, "supernova_charge should be explosive.");
            Assert.Equal("Explosives", supernovaMat.Definition.Category);
            Assert.Equal(100, supernovaMat.Definition.ExplosionRadius);
            Assert.True(supernovaMat.Definition.ExplosionFireCount >= 1000);
            Assert.Equal(StateOfMatter.MovableSolid, supernovaMat.Definition.State);

            // Test aliases
            Assert.Equal(supernovaIdx, registry.GetIndex("supernova charge"));
            Assert.Equal(supernovaIdx, registry.GetIndex("supernova"));
            Assert.Equal(supernovaIdx, registry.GetIndex("supernova_bomb"));
            Assert.Equal(supernovaIdx, registry.GetIndex("supernova bomb"));

            // 2/3rds screen explosives
            ushort cataclysmIdx = registry.GetIndex("cataclysm_bomb");
            Assert.True(cataclysmIdx > 0, "cataclysm_bomb should be registered.");
            var cataclysmMat = registry.GetMaterial(cataclysmIdx);
            Assert.True(cataclysmMat.Definition.IsExplosive, "cataclysm_bomb should be explosive.");
            Assert.Equal("Explosives", cataclysmMat.Definition.Category);
            Assert.Equal(115, cataclysmMat.Definition.ExplosionRadius);
            Assert.True(cataclysmMat.Definition.ExplosionFireCount >= 2000);
            Assert.Equal(StateOfMatter.Solid, cataclysmMat.Definition.State);

            Assert.Equal(cataclysmIdx, registry.GetIndex("cataclysm bomb"));
            Assert.Equal(cataclysmIdx, registry.GetIndex("cataclysm"));
            Assert.Equal(cataclysmIdx, registry.GetIndex("cataclysm_charge"));
            Assert.Equal(cataclysmIdx, registry.GetIndex("cataclysm charge"));

            ushort hypernovaIdx = registry.GetIndex("hypernova_charge");
            Assert.True(hypernovaIdx > 0, "hypernova_charge should be registered.");
            var hypernovaMat = registry.GetMaterial(hypernovaIdx);
            Assert.True(hypernovaMat.Definition.IsExplosive, "hypernova_charge should be explosive.");
            Assert.Equal("Explosives", hypernovaMat.Definition.Category);
            Assert.Equal(115, hypernovaMat.Definition.ExplosionRadius);
            Assert.True(hypernovaMat.Definition.ExplosionFireCount >= 2000);
            Assert.Equal(StateOfMatter.MovableSolid, hypernovaMat.Definition.State);

            Assert.Equal(hypernovaIdx, registry.GetIndex("hypernova charge"));
            Assert.Equal(hypernovaIdx, registry.GetIndex("hypernova"));
            Assert.Equal(hypernovaIdx, registry.GetIndex("hypernova_bomb"));
            Assert.Equal(hypernovaIdx, registry.GetIndex("hypernova bomb"));
        }

        [Theory]
        [InlineData("inferno_bomb")]
        [InlineData("supernova_charge")]
        public void NewExplosives_DetonateAndSetFireToHalfTheScreen(string explosiveId)
        {
            var registry = MaterialRegistry.CreateDefault();
            // Full screen size (280 x 220 = 61,600 cells)
            int screenWidth = 280;
            int screenHeight = 220;
            int totalScreenCells = screenWidth * screenHeight;

            var grid = new SandGrid(screenWidth, screenHeight, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort explosiveIdx = registry.GetIndex(explosiveId);
            ushort fireIdx = registry.GetIndex("fire");
            ushort sparkIdx = registry.GetIndex("spark");
            ushort woodIdx = registry.GetIndex("wood");

            // Fill screen area with flammable wood background to test screen-wide fire ignition
            for (int y = 0; y < screenHeight; y += 4)
            {
                for (int x = 0; x < screenWidth; x += 4)
                {
                    grid.SetCell(x, y, woodIdx);
                }
            }

            int centerX = screenWidth / 2;
            int centerY = screenHeight / 2;

            // Place explosive at center
            grid.SetCell(centerX, centerY, explosiveIdx);

            // Detonate explosive
            engine.DetonateAt(centerX, centerY, explosiveIdx);

            // Calculate blast coverage: count cells within blast radius (r = 100)
            int blastCellsAffected = 0;
            int blastRadius = registry.GetMaterial(explosiveIdx).Definition.ExplosionRadius;
            int blastRadiusSq = blastRadius * blastRadius;

            int fireOrSparkCount = 0;
            int burningFlammablesCount = 0;

            for (int y = 0; y < screenHeight; y++)
            {
                int dy = y - centerY;
                int dy2 = dy * dy;
                for (int x = 0; x < screenWidth; x++)
                {
                    int dx = x - centerX;
                    int d2 = dx * dx + dy2;

                    if (d2 <= blastRadiusSq)
                    {
                        blastCellsAffected++;
                    }

                    ref var cell = ref grid.GetCell(x, y);
                    if (cell.MaterialIndex == fireIdx || cell.MaterialIndex == sparkIdx)
                    {
                        fireOrSparkCount++;
                    }
                    else if (cell.IsBurning)
                    {
                        burningFlammablesCount++;
                    }
                }
            }

            // Verify blast coverage is ~half the screen (approx 50% of screen)
            double blastCoverageRatio = (double)blastCellsAffected / totalScreenCells;
            Assert.True(blastCoverageRatio >= 0.45 && blastCoverageRatio <= 0.55,
                $"Blast radius {blastRadius} should cover approximately half the screen (expected ~0.50, got {blastCoverageRatio:P1}).");

            // Verify massive fire generation across the blast area
            Assert.True(fireOrSparkCount > 500, $"Explosive should generate dense fire particles (got {fireOrSparkCount}).");
            Assert.True(burningFlammablesCount > 500, $"Explosive should ignite flammables across the blast radius (got {burningFlammablesCount}).");
        }

        [Theory]
        [InlineData("cataclysm_bomb")]
        [InlineData("hypernova_charge")]
        public void NewExplosives_DetonateAndSetFireToTwoThirdsOfTheScreen(string explosiveId)
        {
            var registry = MaterialRegistry.CreateDefault();
            // Full screen size (280 x 220 = 61,600 cells)
            int screenWidth = 280;
            int screenHeight = 220;
            int totalScreenCells = screenWidth * screenHeight;

            var grid = new SandGrid(screenWidth, screenHeight, registry, seed: 42);
            var engine = new SimulationEngine(grid);

            ushort explosiveIdx = registry.GetIndex(explosiveId);
            ushort fireIdx = registry.GetIndex("fire");
            ushort sparkIdx = registry.GetIndex("spark");
            ushort woodIdx = registry.GetIndex("wood");

            // Fill screen area with flammable wood background to test screen-wide fire ignition
            for (int y = 0; y < screenHeight; y += 4)
            {
                for (int x = 0; x < screenWidth; x += 4)
                {
                    grid.SetCell(x, y, woodIdx);
                }
            }

            int centerX = screenWidth / 2;
            int centerY = screenHeight / 2;

            // Place explosive at center
            grid.SetCell(centerX, centerY, explosiveIdx);

            // Detonate explosive
            engine.DetonateAt(centerX, centerY, explosiveIdx);

            // Calculate blast coverage: count cells within blast radius (r = 115)
            int blastCellsAffected = 0;
            int blastRadius = registry.GetMaterial(explosiveIdx).Definition.ExplosionRadius;
            int blastRadiusSq = blastRadius * blastRadius;

            int fireOrSparkCount = 0;
            int burningFlammablesCount = 0;

            for (int y = 0; y < screenHeight; y++)
            {
                int dy = y - centerY;
                int dy2 = dy * dy;
                for (int x = 0; x < screenWidth; x++)
                {
                    int dx = x - centerX;
                    int d2 = dx * dx + dy2;

                    if (d2 <= blastRadiusSq)
                    {
                        blastCellsAffected++;
                    }

                    ref var cell = ref grid.GetCell(x, y);
                    if (cell.MaterialIndex == fireIdx || cell.MaterialIndex == sparkIdx)
                    {
                        fireOrSparkCount++;
                    }
                    else if (cell.IsBurning)
                    {
                        burningFlammablesCount++;
                    }
                }
            }

            // Verify blast coverage is ~2/3rds of the screen (approx 66.7% of screen)
            double blastCoverageRatio = (double)blastCellsAffected / totalScreenCells;
            Assert.True(blastCoverageRatio >= 0.62 && blastCoverageRatio <= 0.70,
                $"Blast radius {blastRadius} should cover approximately 2/3rds of the screen (expected ~0.667, got {blastCoverageRatio:P1}).");

            // Verify massive fire generation across the blast area
            Assert.True(fireOrSparkCount > 800, $"Explosive should generate dense fire particles (got {fireOrSparkCount}).");
            Assert.True(burningFlammablesCount > 800, $"Explosive should ignite flammables across the blast radius (got {burningFlammablesCount}).");
        }
    }
}
