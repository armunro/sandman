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
            Assert.True(temp < 200.0f, $"Heated sand should cool down over time. Actual: {temp}°C");
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

            Assert.True(airLeft < 40.0f, $"Surrounding air should dissipate heat towards ambient. Actual: {airLeft}°C");
            Assert.True(airRight < 40.0f, $"Surrounding air should dissipate heat towards ambient. Actual: {airRight}°C");
            Assert.True(airAbove < 60.0f, $"Surrounding air column should dissipate heat towards ambient. Actual: {airAbove}°C");
            Assert.True(stoneTemp < 150.0f, $"Surrounding stone should dissipate heat towards ambient. Actual: {stoneTemp}°C");
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

            Assert.True(ironTemp < 150.0f, $"Iron should have dissipated heat. Actual: {ironTemp}°C");
            Assert.True(stoneTemp < 200.0f, $"Stone should have dissipated heat. Actual: {stoneTemp}°C");
            Assert.True(woodTemp < 150.0f, $"Wood should have dissipated heat. Actual: {woodTemp}°C");
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

            // All solids should fully normalize to room temperature (20.0°C)
            float ironTemp = grid.GetCell(5, 15).Temperature;
            float stoneTemp = grid.GetCell(10, 15).Temperature;
            float basaltTemp = grid.GetCell(15, 15).Temperature;
            float glassTemp = grid.GetCell(20, 15).Temperature;

            Assert.Equal(20.0f, ironTemp);
            Assert.Equal(20.0f, stoneTemp);
            Assert.Equal(20.0f, basaltTemp);
            Assert.Equal(20.0f, glassTemp);
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

            // Verify all cells in the block normalized to room temperature (20.0°C)
            for (int y = 13; y <= 17; y++)
            {
                for (int x = 13; x <= 17; x++)
                {
                    float cellTemp = grid.GetCell(x, y).Temperature;
                    Assert.Equal(20.0f, cellTemp);
                }
            }
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

            // Verify sand normalized to room temperature
            for (int y = 18; y < 25; y++)
            {
                for (int x = 10; x <= 14; x++)
                {
                    ref var cell = ref grid.GetCell(x, y);
                    if (cell.MaterialIndex == sandIdx)
                    {
                        Assert.Equal(20.0f, cell.Temperature);
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

            // Run 30 steps: should radiate and drop significantly
            for (int i = 0; i < 30; i++) engine.Step();
            float t30 = grid.GetCell(10, 10).Temperature;
            Assert.True(t30 < initialTemp, $"Temperature should decrease after 30 steps. Got {t30}°C");

            // Run another 30 steps: should continue to drop
            for (int i = 0; i < 30; i++) engine.Step();
            float t60 = grid.GetCell(10, 10).Temperature;
            Assert.True(t60 < t30, $"Temperature should continue decreasing after 60 steps. Got {t60}°C");

            // Run another 80 steps: should reach room temperature (20.0°C)
            for (int i = 0; i < 80; i++) engine.Step();
            float t140 = grid.GetCell(10, 10).Temperature;
            Assert.Equal(20.0f, t140);
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

            // Step further and verify solid iron continues to dissipate heat to ambient
            for (int i = 0; i < 150; i++)
            {
                engine.Step();
            }

            float finalTemp = grid.GetCell(10, 19).Temperature;
            Assert.True(finalTemp < frozenTemp, $"Solid iron should continue dissipating heat. Got {finalTemp}°C vs {frozenTemp}°C");
            Assert.Equal(20.0f, finalTemp);
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
            
            // Slow decay simulation
            var gridSlow = new SandGrid(20, 20, registry, seed: 42);
            var engineSlow = new SimulationEngine(gridSlow) { TemperatureDecayRate = 0.1f };
            ushort ironIdx = registry.GetIndex("iron");
            gridSlow.SetCell(10, 10, ironIdx, temperature: 800.0f);

            // Fast decay simulation
            var gridFast = new SandGrid(20, 20, registry, seed: 42);
            var engineFast = new SimulationEngine(gridFast) { TemperatureDecayRate = 2.5f };
            gridFast.SetCell(10, 10, ironIdx, temperature: 800.0f);

            // Step both for 40 ticks
            for (int i = 0; i < 40; i++)
            {
                engineSlow.Step();
                engineFast.Step();
            }

            float tempSlow = gridSlow.GetCell(10, 10).Temperature;
            float tempFast = gridFast.GetCell(10, 10).Temperature;

            Assert.True(tempFast < tempSlow, $"Fast decay rate should cool significantly faster than slow decay rate. Fast: {tempFast}°C, Slow: {tempSlow}°C");
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
    }
}
