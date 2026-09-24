using System;
using System.Collections.Generic;
using Sandman.Core.Config;
using Sandman.Core.Models;

namespace Sandman.Core.Simulation
{
    public class SimulationEngine
    {
        public SandGrid Grid { get; }
        private readonly float[] _tempDeltas;
        private readonly Queue<(int X, int Y, ushort MaterialIndex)> _explosionQueue = new();
        private bool _scanLeftToRight = true;
        private ulong _tickCount = 0;

        public ulong TickCount => _tickCount;
        public bool IsRunning { get; set; } = true;
        public int StepsPerTick { get; set; } = 1;
        public bool Fallout { get; set; } = false;
        public bool FallThroughBottom
        {
            get => Fallout;
            set => Fallout = value;
        }
        public float AmbientTemperature
        {
            get => Grid.AmbientTemperature;
            set => Grid.AmbientTemperature = value;
        }
        public float TemperatureDecayRate { get; set; } = 1.0f;

        public SimulationEngine(SandGrid grid)
        {
            Grid = grid;
            _tempDeltas = new float[grid.Width * grid.Height];
        }

        public void Step()
        {
            _tickCount++;
            _scanLeftToRight = !_scanLeftToRight;

            // Clear update flags
            for (int i = 0; i < Grid.Cells.Length; i++)
            {
                Grid.Cells[i].SetUpdated(false);
                _tempDeltas[i] = 0;
            }

            // 1. Phase transitions, ignition & explosives trigger
            SimulatePhaseChangesAndCombustion();

            // 2. Process any immediate explosions triggered by phase changes / ignition
            ProcessExplosions();

            // 3. Particle cellular automata physics, movement & chemical reactions
            SimulateParticles();

            // Process explosions triggered during particle interactions / chemical reactions
            ProcessExplosions();

            // 4. Thermodynamics & heat diffusion
            SimulateThermodynamics();

            // 5. Process any secondary explosions triggered during motion/thermodynamics
            ProcessExplosions();
        }

        private void SimulateParticles()
        {
            int w = Grid.Width;
            int h = Grid.Height;
            var reg = Grid.Registry;
            var rand = Grid.Random;

            int startX = _scanLeftToRight ? 0 : w - 1;
            int endX = _scanLeftToRight ? w : -1;
            int stepX = _scanLeftToRight ? 1 : -1;

            // Bottom-up scan
            for (int y = h - 1; y >= 0; y--)
            {
                for (int x = startX; x != endX; x += stepX)
                {
                    int idx = y * w + x;
                    ref var cell = ref Grid.Cells[idx];

                    if (cell.IsEmpty || cell.HasUpdated)
                        continue;

                    var mat = reg.GetMaterial(cell.MaterialIndex);

                    // Emitter
                    if (mat.Definition.IsEmitter && mat.EmitsMaterialIndex > 0)
                    {
                        TryEmit(x, y, mat.EmitsMaterialIndex);
                        continue;
                    }

                    // Drain
                    if (mat.Definition.IsDrain)
                    {
                        ConsumeNeighbors(x, y);
                        continue;
                    }

                    // Burning behavior
                    if (cell.IsBurning)
                    {
                        SimulateBurningCell(x, y, mat, ref cell);
                    }

                    // Ephemeral lifetime decay
                    if (mat.Definition.Lifetime > 0 && !cell.IsBurning)
                    {
                        if (cell.Life > 0)
                        {
                            cell.Life--;
                            if (cell.Life == 0)
                            {
                                if (mat.DecayTargetIndex > 0)
                                {
                                    Grid.SetCell(x, y, mat.DecayTargetIndex, (float?)cell.Temperature);
                                }
                                else
                                {
                                    Grid.SetEmpty(x, y);
                                }
                                continue;
                            }
                        }
                    }

                    // Chemical reactions (Acid, Fire vs Water, Water vs Lava)
                    if (SimulateChemicalReactions(x, y, mat, ref cell))
                    {
                        continue;
                    }

                    // Movement physics based on StateOfMatter
                    switch (mat.Definition.State)
                    {
                        case StateOfMatter.MovableSolid:
                            SimulatePowder(x, y, mat);
                            break;

                        case StateOfMatter.Liquid:
                            SimulateLiquid(x, y, mat);
                            break;

                        case StateOfMatter.Gas:
                            SimulateGas(x, y, mat);
                            break;

                        case StateOfMatter.Energy:
                            SimulateEnergy(x, y, mat);
                            break;
                    }
                }
            }
        }

        private void SimulateBurningCell(int x, int y, MaterialRuntime mat, ref Cell cell)
        {
            var rand = Grid.Random;
            cell.Temperature += mat.Definition.HeatGenerated;

            // Emit smoke occasionally
            if (rand.NextDouble() < mat.Definition.SmokeChance && Grid.InBounds(x, y - 1))
            {
                ref var above = ref Grid.GetCell(x, y - 1);
                if (above.IsEmpty)
                {
                    ushort smokeIdx = Grid.Registry.GetIndex("smoke");
                    if (smokeIdx > 0)
                    {
                        Grid.SetCell(x, y - 1, smokeIdx, (float?)(cell.Temperature * 0.7f));
                    }
                }
            }

            // Emit fire / sparks occasionally
            if (rand.NextDouble() < mat.Definition.FireChance && Grid.InBounds(x, y - 1))
            {
                ref var above = ref Grid.GetCell(x, y - 1);
                if (above.IsEmpty)
                {
                    ushort sparkIdx = Grid.Registry.GetIndex("spark");
                    ushort fireIdx = Grid.Registry.GetIndex("fire");
                    ushort emitIdx = (sparkIdx > 0 && rand.Next(2) == 0) ? sparkIdx : (fireIdx > 0 ? fireIdx : MaterialRegistry.EmptyIndex);
                    if (emitIdx > 0)
                    {
                        Grid.SetCell(x, y - 1, emitIdx, (float?)cell.Temperature);
                    }
                }
            }

            // Heat and ignite neighboring flammable materials
            IgniteNeighbors(x, y, cell.Temperature);

            if (cell.Life > 0)
            {
                cell.Life--;
                if (cell.Life == 0)
                {
                    if (mat.BurnProductIndex > 0)
                    {
                        Grid.SetCell(x, y, mat.BurnProductIndex, (float?)cell.Temperature);
                    }
                    else
                    {
                        Grid.SetEmpty(x, y);
                    }
                }
            }
        }

        private void IgniteNeighbors(int x, int y, float heatSourceTemp)
        {
            int[] dx = { 0, 0, 1, -1, 1, -1, 1, -1 };
            int[] dy = { 1, -1, 0, 0, 1, -1, -1, 1 };

            for (int i = 0; i < 8; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];

                if (Grid.InBounds(nx, ny))
                {
                    ref var nCell = ref Grid.GetCell(nx, ny);
                    if (!nCell.IsEmpty)
                    {
                        var nMat = Grid.Registry.GetMaterial(nCell.MaterialIndex);
                        if (nMat.Definition.IsFlammable && !nCell.IsBurning && (nCell.Temperature >= nMat.Definition.IgnitionTemperature || heatSourceTemp >= nMat.Definition.IgnitionTemperature))
                        {
                            if (nMat.Definition.IsExplosive)
                            {
                                _explosionQueue.Enqueue((nx, ny, nCell.MaterialIndex));
                            }
                            else
                            {
                                nCell.SetBurning(true);
                                if (nMat.Definition.IsInstantFuse)
                                {
                                    nCell.Life = 1;
                                    PropagateInstantFuse(nx, ny);
                                }
                                else
                                {
                                    int baseFuel = nMat.Definition.Fuel;
                                    int minFuel = Math.Max(1, (int)(baseFuel * 0.7f));
                                    int maxFuel = Math.Max(minFuel + 1, (int)(baseFuel * 1.3f) + 1);
                                    nCell.Life = (ushort)Grid.Random.Next(minFuel, maxFuel);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void PropagateInstantFuse(int startX, int startY)
        {
            var reg = Grid.Registry;
            var queue = new Queue<(int x, int y)>();
            queue.Enqueue((startX, startY));

            int[] dx = { 0, 0, 1, -1, 1, -1, 1, -1 };
            int[] dy = { 1, -1, 0, 0, 1, -1, -1, 1 };

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();
                for (int i = 0; i < 8; i++)
                {
                    int nx = cx + dx[i];
                    int ny = cy + dy[i];

                    if (Grid.InBounds(nx, ny))
                    {
                        ref var nCell = ref Grid.GetCell(nx, ny);
                        if (!nCell.IsEmpty)
                        {
                            var nMat = reg.GetMaterial(nCell.MaterialIndex);
                            if (nMat.Definition.IsInstantFuse && !nCell.IsBurning)
                            {
                                nCell.SetBurning(true);
                                nCell.Life = 1;
                                nCell.Temperature = MathF.Max(nCell.Temperature, 1000.0f);
                                queue.Enqueue((nx, ny));
                            }
                            else if (nMat.Definition.IsExplosive)
                            {
                                _explosionQueue.Enqueue((nx, ny, nCell.MaterialIndex));
                            }
                            else if (nMat.Definition.IsFlammable && !nCell.IsBurning)
                            {
                                nCell.SetBurning(true);
                                nCell.Temperature = MathF.Max(nCell.Temperature, nMat.Definition.IgnitionTemperature + 100.0f);
                                int baseFuel = nMat.Definition.Fuel;
                                int minFuel = Math.Max(1, (int)(baseFuel * 0.7f));
                                int maxFuel = Math.Max(minFuel + 1, (int)(baseFuel * 1.3f) + 1);
                                nCell.Life = (ushort)Grid.Random.Next(minFuel, maxFuel);
                            }
                        }
                    }
                }
            }

            ProcessExplosions();
        }

        private bool SimulateChemicalReactions(int x, int y, MaterialRuntime mat, ref Cell cell)
        {
            var rand = Grid.Random;

            // Acid dissolving
            if (mat.Definition.Id.Equals("acid", StringComparison.OrdinalIgnoreCase))
            {
                int[] dx = { 0, 0, 1, -1 };
                int[] dy = { 1, -1, 0, 0 };

                for (int i = 0; i < 4; i++)
                {
                    int nx = x + dx[i];
                    int ny = y + dy[i];

                    if (Grid.InBounds(nx, ny))
                    {
                        ref var nCell = ref Grid.GetCell(nx, ny);
                        if (!nCell.IsEmpty)
                        {
                            var nMat = Grid.Registry.GetMaterial(nCell.MaterialIndex);
                            if (!nMat.Definition.Id.Equals("acid", StringComparison.OrdinalIgnoreCase) &&
                                !nMat.Definition.FixedTemperature &&
                                nMat.Definition.AcidResistance < 1.0f)
                            {
                                if (rand.NextDouble() > nMat.Definition.AcidResistance * 0.9f)
                                {
                                    Grid.SetEmpty(nx, ny);
                                    if (rand.Next(3) == 0)
                                    {
                                        Grid.SetEmpty(x, y);
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Water extinguishing fire or interacting with lava
            if (mat.Definition.ExtinguishesFire)
            {
                int[] dx = { 0, 0, 1, -1 };
                int[] dy = { 1, -1, 0, 0 };

                for (int i = 0; i < 4; i++)
                {
                    int nx = x + dx[i];
                    int ny = y + dy[i];

                    if (Grid.InBounds(nx, ny))
                    {
                        ref var nCell = ref Grid.GetCell(nx, ny);
                        if (!nCell.IsEmpty)
                        {
                            var nMat = Grid.Registry.GetMaterial(nCell.MaterialIndex);

                            // Extinguish fire
                            if (nMat.Definition.State == StateOfMatter.Energy || (nCell.IsBurning && !nMat.Definition.IsWaterproof))
                            {
                                nCell.SetBurning(false);
                                if (nMat.Definition.State == StateOfMatter.Energy)
                                {
                                    Grid.SetEmpty(nx, ny);
                                }
                                // Turn water into steam if very hot
                                if (cell.Temperature > 80.0f)
                                {
                                    ushort steamIdx = Grid.Registry.GetIndex("steam");
                                    if (steamIdx > 0)
                                    {
                                        Grid.SetCell(x, y, steamIdx, 105.0f);
                                        return true;
                                    }
                                }
                            }

                            // Water + Lava interaction
                            if (nMat.Definition.Id.Equals("lava", StringComparison.OrdinalIgnoreCase))
                            {
                                ushort basaltIdx = Grid.Registry.GetIndex("basalt");
                                ushort steamIdx = Grid.Registry.GetIndex("steam");
                                if (basaltIdx > 0) Grid.SetCell(nx, ny, basaltIdx, 600.0f);
                                if (steamIdx > 0)
                                {
                                    Grid.SetCell(x, y, steamIdx, 120.0f);
                                    return true;
                                }
                            }
                        }
                    }
                }
            }

            // Contact chemical reactions (e.g. Binary Explosives, acid/base reactions, custom reactions)
            if (mat.ReactsWithIndex > 0)
            {
                int[] rx = { 0, 0, 1, -1, 1, -1, 1, -1 };
                int[] ry = { 1, -1, 0, 0, 1, -1, -1, 1 };

                for (int i = 0; i < 8; i++)
                {
                    int nx = x + rx[i];
                    int ny = y + ry[i];

                    if (Grid.InBounds(nx, ny))
                    {
                        ref var nCell = ref Grid.GetCell(nx, ny);
                        if (!nCell.IsEmpty && nCell.MaterialIndex == mat.ReactsWithIndex)
                        {
                            if (mat.Definition.ExplodesOnReaction || mat.Definition.IsExplosive)
                            {
                                _explosionQueue.Enqueue((x, y, cell.MaterialIndex));
                                Grid.SetEmpty(x, y);
                                return true;
                            }
                            else if (mat.ReactionProductIndex > 0)
                            {
                                Grid.SetCell(x, y, mat.ReactionProductIndex, (float?)cell.Temperature);
                                Grid.SetCell(nx, ny, mat.ReactionProductIndex, (float?)nCell.Temperature);
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private void SimulatePowder(int x, int y, MaterialRuntime mat)
        {
            // 1. Directly below
            if (TryMoveOrSwap(x, y, x, y + 1, mat))
                return;

            // 2. Diagonals below
            bool leftFirst = Grid.Random.Next(2) == 0;
            int dir1 = leftFirst ? -1 : 1;
            int dir2 = -dir1;

            if (TryMoveOrSwap(x, y, x + dir1, y + 1, mat))
                return;

            if (TryMoveOrSwap(x, y, x + dir2, y + 1, mat))
                return;
        }

        private void SimulateLiquid(int x, int y, MaterialRuntime mat)
        {
            // 1. Directly below
            if (TryMoveOrSwap(x, y, x, y + 1, mat))
                return;

            // 2. Diagonals below
            bool leftFirst = Grid.Random.Next(2) == 0;
            int dir1 = leftFirst ? -1 : 1;
            int dir2 = -dir1;

            if (TryMoveOrSwap(x, y, x + dir1, y + 1, mat))
                return;

            if (TryMoveOrSwap(x, y, x + dir2, y + 1, mat))
                return;

            // 3. Horizontal dispersion (cannot pass through obstacles)
            int disp = Math.Max(1, mat.Definition.Dispersion);
            bool blocked1 = false;
            bool blocked2 = false;

            for (int d = 1; d <= disp; d++)
            {
                if (!blocked1)
                {
                    int nx = x + dir1 * d;
                    if (TryMoveOrSwap(x, y, nx, y, mat))
                        return;
                    if (!Grid.InBounds(nx, y) || !Grid.GetCell(nx, y).IsEmpty)
                        blocked1 = true;
                }

                if (!blocked2)
                {
                    int nx = x + dir2 * d;
                    if (TryMoveOrSwap(x, y, nx, y, mat))
                        return;
                    if (!Grid.InBounds(nx, y) || !Grid.GetCell(nx, y).IsEmpty)
                        blocked2 = true;
                }
            }
        }

        private void SimulateGas(int x, int y, MaterialRuntime mat)
        {
            // 1. Directly above
            if (TryMoveOrSwapGas(x, y, x, y - 1, mat))
                return;

            // 2. Diagonals above
            bool leftFirst = Grid.Random.Next(2) == 0;
            int dir1 = leftFirst ? -1 : 1;
            int dir2 = -dir1;

            if (TryMoveOrSwapGas(x, y, x + dir1, y - 1, mat))
                return;

            if (TryMoveOrSwapGas(x, y, x + dir2, y - 1, mat))
                return;

            // 3. Horizontal dispersion / drift (cannot pass through obstacles)
            int disp = Math.Max(1, mat.Definition.Dispersion);
            bool blocked1 = false;
            bool blocked2 = false;

            for (int d = 1; d <= disp; d++)
            {
                if (!blocked1)
                {
                    int nx = x + dir1 * d;
                    if (TryMoveOrSwapGas(x, y, nx, y, mat))
                        return;
                    if (!Grid.InBounds(nx, y) || !Grid.GetCell(nx, y).IsEmpty)
                        blocked1 = true;
                }

                if (!blocked2)
                {
                    int nx = x + dir2 * d;
                    if (TryMoveOrSwapGas(x, y, nx, y, mat))
                        return;
                    if (!Grid.InBounds(nx, y) || !Grid.GetCell(nx, y).IsEmpty)
                        blocked2 = true;
                }
            }
        }

        private void SimulateEnergy(int x, int y, MaterialRuntime mat)
        {
            ref var cell = ref Grid.GetCell(x, y);
            float temp = MathF.Max(cell.Temperature, mat.Definition.DefaultTemperature);
            cell.Temperature = temp;
            IgniteNeighbors(x, y, temp);

            // Flicker upward with random displacement
            int r = Grid.Random.Next(5);
            int nx = x + (Grid.Random.Next(3) - 1);
            int ny = y - (r > 0 ? 1 : 0);

            if (Fallout && !Grid.InBounds(nx, ny) && Grid.InBounds(x, y))
            {
                Grid.SetEmpty(x, y);
                return;
            }

            if (Grid.InBounds(nx, ny))
            {
                ref var target = ref Grid.GetCell(nx, ny);
                if (target.IsEmpty)
                {
                    SwapCells(x, y, nx, ny);
                    Grid.GetCell(nx, ny).SetUpdated(true);
                }
            }
        }

        private bool TryMoveOrSwap(int fromX, int fromY, int toX, int toY, MaterialRuntime fromMat)
        {
            if (Fallout && !Grid.InBounds(toX, toY) && Grid.InBounds(fromX, fromY))
            {
                Grid.SetEmpty(fromX, fromY);
                return true;
            }

            if (!Grid.InBounds(toX, toY)) return false;

            ref var target = ref Grid.GetCell(toX, toY);
            if (target.IsEmpty)
            {
                SwapCells(fromX, fromY, toX, toY);
                Grid.GetCell(toX, toY).SetUpdated(true);
                return true;
            }

            var targetMat = Grid.Registry.GetMaterial(target.MaterialIndex);

            // If target is a torch, allow powders/movable solids and liquids to fall straight through the torch
            if (targetMat.IsTorch && (fromMat.Definition.State == StateOfMatter.MovableSolid || fromMat.Definition.State == StateOfMatter.Liquid))
            {
                int dy = (toY >= fromY) ? 1 : -1;
                int passY = toY;
                while (Grid.InBounds(toX, passY) && Grid.Registry.GetMaterial(Grid.GetCell(toX, passY).MaterialIndex).IsTorch)
                {
                    passY += dy;
                }

                if (Fallout && !Grid.InBounds(toX, passY) && Grid.InBounds(fromX, fromY))
                {
                    Grid.SetEmpty(fromX, fromY);
                    return true;
                }

                if (Grid.InBounds(toX, passY))
                {
                    ref var belowTorch = ref Grid.GetCell(toX, passY);
                    if (belowTorch.IsEmpty)
                    {
                        SwapCells(fromX, fromY, toX, passY);
                        Grid.GetCell(toX, passY).SetUpdated(true);
                        return true;
                    }

                    var belowMat = Grid.Registry.GetMaterial(belowTorch.MaterialIndex);
                    if (belowMat.Definition.State == StateOfMatter.Liquid ||
                        belowMat.Definition.State == StateOfMatter.Gas ||
                        belowMat.Definition.State == StateOfMatter.Energy)
                    {
                        if (fromMat.Definition.Density > belowMat.Definition.Density)
                        {
                            SwapCells(fromX, fromY, toX, passY);
                            Grid.GetCell(toX, passY).SetUpdated(true);
                            return true;
                        }
                    }
                }

                return false;
            }

            // Move through/sink through lighter liquids, gases, or energy
            if (targetMat.Definition.State == StateOfMatter.Liquid ||
                targetMat.Definition.State == StateOfMatter.Gas ||
                targetMat.Definition.State == StateOfMatter.Energy)
            {
                if (fromMat.Definition.Density > targetMat.Definition.Density)
                {
                    SwapCells(fromX, fromY, toX, toY);
                    Grid.GetCell(toX, toY).SetUpdated(true);
                    return true;
                }
            }

            return false;
        }

        private bool TryMoveOrSwapGas(int fromX, int fromY, int toX, int toY, MaterialRuntime fromMat)
        {
            if (Fallout && !Grid.InBounds(toX, toY) && Grid.InBounds(fromX, fromY))
            {
                Grid.SetEmpty(fromX, fromY);
                return true;
            }

            if (!Grid.InBounds(toX, toY)) return false;

            ref var target = ref Grid.GetCell(toX, toY);
            if (target.IsEmpty)
            {
                SwapCells(fromX, fromY, toX, toY);
                Grid.GetCell(toX, toY).SetUpdated(true);
                return true;
            }

            var targetMat = Grid.Registry.GetMaterial(target.MaterialIndex);
            // Rise / bubble through heavier gases, liquids, or energy
            if ((targetMat.Definition.State == StateOfMatter.Gas ||
                 targetMat.Definition.State == StateOfMatter.Liquid ||
                 targetMat.Definition.State == StateOfMatter.Energy) &&
                fromMat.Definition.Density < targetMat.Definition.Density)
            {
                SwapCells(fromX, fromY, toX, toY);
                Grid.GetCell(toX, toY).SetUpdated(true);
                return true;
            }

            return false;
        }

        private void SwapCells(int x1, int y1, int x2, int y2)
        {
            ref var c1 = ref Grid.GetCell(x1, y1);
            ref var c2 = ref Grid.GetCell(x2, y2);

            if (c1.IsEmpty && !c2.IsEmpty)
            {
                float matTemp = c2.Temperature;
                float airTemp = c1.Temperature;
                var temp = c1;
                c1 = c2;
                c2 = temp;
                // Newly vacated empty cell retains a warm air wake
                c2.Temperature = airTemp + (matTemp - airTemp) * 0.15f;
            }
            else if (!c1.IsEmpty && c2.IsEmpty)
            {
                float matTemp = c1.Temperature;
                float airTemp = c2.Temperature;
                var temp = c1;
                c1 = c2;
                c2 = temp;
                // Newly vacated empty cell retains a warm air wake
                c1.Temperature = airTemp + (matTemp - airTemp) * 0.15f;
            }
            else
            {
                var temp = c1;
                c1 = c2;
                c2 = temp;
            }
        }

        private void TryEmit(int x, int y, ushort emitMatIndex)
        {
            var emitMat = Grid.Registry.GetMaterial(emitMatIndex);
            bool upwardBias = emitMat.Definition.State == StateOfMatter.Energy || emitMat.Definition.State == StateOfMatter.Gas;

            int[] dx = upwardBias
                ? new int[] { 0, -1, 1, -1, 1, 0, -1, 1 }
                : new int[] { 0, -1, 1, -1, 1, 0, -1, 1 };
            int[] dy = upwardBias
                ? new int[] { -1, -1, -1, 0, 0, 1, 1, 1 }
                : new int[] { 1, 1, 1, 0, 0, -1, -1, -1 };

            for (int i = 0; i < 8; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];

                if (Grid.InBounds(nx, ny))
                {
                    ref var cell = ref Grid.GetCell(nx, ny);
                    if (cell.IsEmpty)
                    {
                        Grid.SetCell(nx, ny, emitMatIndex);
                        break;
                    }
                    else if (emitMat.Definition.State != StateOfMatter.Gas &&
                             Grid.Registry.GetMaterial(cell.MaterialIndex).Definition.State == StateOfMatter.Gas)
                    {
                        Grid.SetCell(nx, ny, emitMatIndex);
                        break;
                    }
                }
            }
        }

        private void ConsumeNeighbors(int x, int y)
        {
            int[] dx = { 0, 0, 1, -1, 1, -1, 1, -1 };
            int[] dy = { 1, -1, 0, 0, 1, -1, -1, 1 };

            for (int i = 0; i < 8; i++)
            {
                int nx = x + dx[i];
                int ny = y + dy[i];

                if (Grid.InBounds(nx, ny))
                {
                    ref var cell = ref Grid.GetCell(nx, ny);
                    if (!cell.IsEmpty)
                    {
                        var mat = Grid.Registry.GetMaterial(cell.MaterialIndex);
                        if (!mat.Definition.FixedTemperature && !mat.Definition.IsDrain)
                        {
                            Grid.SetEmpty(nx, ny);
                        }
                    }
                }
            }
        }

        private void SimulateThermodynamics()
        {
            int w = Grid.Width;
            int h = Grid.Height;
            var reg = Grid.Registry;
            float amb = Grid.AmbientTemperature;
            const float dt = 0.08f;
            const float airConductivity = 0.018f;
            const float airSpecificHeat = 0.15f;

            // 4-way thermal conduction and ambient diffusion
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    ref var cell = ref Grid.Cells[idx];

                    var mat = reg.GetMaterial(cell.MaterialIndex);
                    if (mat.Definition.FixedTemperature)
                    {
                        cell.Temperature = mat.Definition.DefaultTemperature;
                    }

                    float kSelf;
                    float cSelf;
                    if (cell.IsEmpty)
                    {
                        kSelf = airConductivity;
                        cSelf = airSpecificHeat;
                    }
                    else if (mat.Definition.State == StateOfMatter.Energy)
                    {
                        kSelf = mat.Definition.ThermalConductivity;
                        cSelf = 0.65f;
                    }
                    else if (mat.Definition.State == StateOfMatter.Gas)
                    {
                        kSelf = mat.Definition.ThermalConductivity;
                        cSelf = MathF.Max(0.08f, mat.Definition.SpecificHeat * 0.10f);
                    }
                    else
                    {
                        kSelf = mat.Definition.ThermalConductivity;
                        float density = mat.Definition.Density > 0 ? mat.Definition.Density : 1.0f;
                        cSelf = MathF.Max(0.25f, mat.Definition.SpecificHeat * MathF.Sqrt(MathF.Max(0.5f, density)));
                    }
                    bool selfFixed = mat.Definition.FixedTemperature;

                    // Heat exchange with right neighbor
                    if (x + 1 < w)
                    {
                        int rightIdx = idx + 1;
                        ref var rightCell = ref Grid.Cells[rightIdx];
                        var rightMat = reg.GetMaterial(rightCell.MaterialIndex);
                        bool rightFixed = rightMat.Definition.FixedTemperature;

                        if (!selfFixed || !rightFixed)
                        {
                            float kRight;
                            float cRight;
                            if (rightCell.IsEmpty)
                            {
                                kRight = airConductivity;
                                cRight = airSpecificHeat;
                            }
                            else if (rightMat.Definition.State == StateOfMatter.Energy)
                            {
                                kRight = rightMat.Definition.ThermalConductivity;
                                cRight = 0.65f;
                            }
                            else if (rightMat.Definition.State == StateOfMatter.Gas)
                            {
                                kRight = rightMat.Definition.ThermalConductivity;
                                cRight = MathF.Max(0.08f, rightMat.Definition.SpecificHeat * 0.10f);
                            }
                            else
                            {
                                kRight = rightMat.Definition.ThermalConductivity;
                                float density = rightMat.Definition.Density > 0 ? rightMat.Definition.Density : 1.0f;
                                cRight = MathF.Max(0.25f, rightMat.Definition.SpecificHeat * MathF.Sqrt(MathF.Max(0.5f, density)));
                            }

                            float kEff;
                            if (cell.IsEmpty && rightCell.IsEmpty)
                            {
                                kEff = 0.35f; // Strong ambient air-to-air diffusion
                            }
                            else if (cell.IsEmpty || rightCell.IsEmpty)
                            {
                                // Surface convective heat transfer between material and air
                                var nonAirMat = cell.IsEmpty ? rightMat : mat;
                                if (nonAirMat.Definition.ThermalConductivity < 0.0001f)
                                {
                                    kEff = 0.0f; // Perfect thermal insulator
                                }
                                else
                                {
                                    float kMat = nonAirMat.Definition.ThermalConductivity;
                                    if (nonAirMat.Definition.State == StateOfMatter.Liquid)
                                    {
                                        kEff = 0.010f + 0.015f * kMat;
                                    }
                                    else if (nonAirMat.Definition.State == StateOfMatter.Gas)
                                    {
                                        kEff = 0.012f + 0.018f * kMat;
                                    }
                                    else
                                    {
                                        kEff = 0.08f + 0.16f * kMat;
                                    }
                                }
                            }
                            else
                            {
                                // Material-to-material contact conductance
                                if (kSelf < 0.0001f || kRight < 0.0001f)
                                {
                                    kEff = 0.0f; // Perfect thermal insulator (e.g. wall)
                                }
                                else
                                {
                                    float kHarmonic = (2.0f * kSelf * kRight) / (kSelf + kRight + 0.0001f);
                                    bool hasFluidOrEnergy = mat.Definition.State == StateOfMatter.Liquid || mat.Definition.State == StateOfMatter.Energy ||
                                                            rightMat.Definition.State == StateOfMatter.Liquid || rightMat.Definition.State == StateOfMatter.Energy;
                                    if (hasFluidOrEnergy)
                                    {
                                        // Vigorous immersion/wetting thermal flux for molten liquids & hot fluids
                                        kEff = MathF.Max(kHarmonic * 3.0f, 2.5f * MathF.Max(kSelf, kRight));
                                    }
                                    else
                                    {
                                        kEff = kHarmonic;
                                    }
                                }
                            }

                            float deltaT = rightCell.Temperature - cell.Temperature;
                            float deltaQ = deltaT * (kEff * dt);
                            float cEff = (selfFixed || mat.Definition.State == StateOfMatter.Energy) ? cRight :
                                         (rightFixed || rightMat.Definition.State == StateOfMatter.Energy) ? cSelf :
                                         MathF.Min(cSelf, cRight);
                            float maxQ = 0.25f * cEff * MathF.Abs(deltaT);
                            deltaQ = Math.Clamp(deltaQ, -maxQ, maxQ);

                            if (!selfFixed && mat.Definition.State != StateOfMatter.Energy) _tempDeltas[idx] += deltaQ / cSelf;
                            if (!rightFixed && rightMat.Definition.State != StateOfMatter.Energy) _tempDeltas[rightIdx] -= deltaQ / cRight;
                        }
                    }

                    // Heat exchange with bottom neighbor
                    if (y + 1 < h)
                    {
                        int bottomIdx = idx + w;
                        ref var bottomCell = ref Grid.Cells[bottomIdx];
                        var bottomMat = reg.GetMaterial(bottomCell.MaterialIndex);
                        bool bottomFixed = bottomMat.Definition.FixedTemperature;

                        if (!selfFixed || !bottomFixed)
                        {
                            float kBottom;
                            float cBottom;
                            if (bottomCell.IsEmpty)
                            {
                                kBottom = airConductivity;
                                cBottom = airSpecificHeat;
                            }
                            else if (bottomMat.Definition.State == StateOfMatter.Energy)
                            {
                                kBottom = bottomMat.Definition.ThermalConductivity;
                                cBottom = 0.65f;
                            }
                            else if (bottomMat.Definition.State == StateOfMatter.Gas)
                            {
                                kBottom = bottomMat.Definition.ThermalConductivity;
                                cBottom = MathF.Max(0.08f, bottomMat.Definition.SpecificHeat * 0.10f);
                            }
                            else
                            {
                                kBottom = bottomMat.Definition.ThermalConductivity;
                                float density = bottomMat.Definition.Density > 0 ? bottomMat.Definition.Density : 1.0f;
                                cBottom = MathF.Max(0.25f, bottomMat.Definition.SpecificHeat * MathF.Sqrt(MathF.Max(0.5f, density)));
                            }

                            float kEff;
                            if (cell.IsEmpty && bottomCell.IsEmpty)
                            {
                                kEff = 0.35f; // Strong ambient air-to-air diffusion
                            }
                            else if (cell.IsEmpty || bottomCell.IsEmpty)
                            {
                                // Surface convective heat transfer between material and air
                                var nonAirMat = cell.IsEmpty ? bottomMat : mat;
                                if (nonAirMat.Definition.ThermalConductivity < 0.0001f)
                                {
                                    kEff = 0.0f; // Perfect thermal insulator
                                }
                                else
                                {
                                    float kMat = nonAirMat.Definition.ThermalConductivity;
                                    if (nonAirMat.Definition.State == StateOfMatter.Liquid)
                                    {
                                        kEff = 0.010f + 0.015f * kMat;
                                    }
                                    else if (nonAirMat.Definition.State == StateOfMatter.Gas)
                                    {
                                        kEff = 0.012f + 0.018f * kMat;
                                    }
                                    else
                                    {
                                        kEff = 0.08f + 0.16f * kMat;
                                    }
                                }
                            }
                            else
                            {
                                // Material-to-material contact conductance
                                if (kSelf < 0.0001f || kBottom < 0.0001f)
                                {
                                    kEff = 0.0f; // Perfect thermal insulator (e.g. wall)
                                }
                                else
                                {
                                    float kHarmonic = (2.0f * kSelf * kBottom) / (kSelf + kBottom + 0.0001f);
                                    bool hasFluidOrEnergy = mat.Definition.State == StateOfMatter.Liquid || mat.Definition.State == StateOfMatter.Energy ||
                                                            bottomMat.Definition.State == StateOfMatter.Liquid || bottomMat.Definition.State == StateOfMatter.Energy;
                                    if (hasFluidOrEnergy)
                                    {
                                        // Vigorous immersion/wetting thermal flux for molten liquids & hot fluids
                                        kEff = MathF.Max(kHarmonic * 3.0f, 2.5f * MathF.Max(kSelf, kBottom));
                                    }
                                    else
                                    {
                                        kEff = kHarmonic;
                                    }
                                }
                            }

                            float kFactor = kEff * dt;
                            // Upward convection boost: hot air/gas and hot solids vigorously rise heat into air above
                            if (bottomCell.Temperature > cell.Temperature)
                            {
                                if (bottomMat.Definition.State == StateOfMatter.Energy)
                                {
                                    kFactor *= 1.8f; // Hot rising flame transfers heat upward to overhead structures
                                }
                                else if (cell.IsEmpty && bottomCell.IsEmpty)
                                {
                                    kFactor *= 1.6f; // Hot air rises through air
                                }
                                else if (cell.IsEmpty && bottomMat.Definition.State != StateOfMatter.Liquid)
                                {
                                    kFactor *= 1.5f; // Hot solid/gas transfers vigorous convective heat to air directly above it
                                }
                                else if (mat.Definition.State == StateOfMatter.Liquid)
                                {
                                    kFactor *= 1.4f; // Vigorous upward convective cooling into fluid above
                                }
                            }

                            float deltaT = bottomCell.Temperature - cell.Temperature;
                            float deltaQ = deltaT * kFactor;
                            float cEff = (selfFixed || mat.Definition.State == StateOfMatter.Energy) ? cBottom :
                                         (bottomFixed || bottomMat.Definition.State == StateOfMatter.Energy) ? cSelf :
                                         MathF.Min(cSelf, cBottom);
                            float maxQ = 0.25f * cEff * MathF.Abs(deltaT);
                            deltaQ = Math.Clamp(deltaQ, -maxQ, maxQ);

                            if (!selfFixed && mat.Definition.State != StateOfMatter.Energy) _tempDeltas[idx] += deltaQ / cSelf;
                            if (!bottomFixed && bottomMat.Definition.State != StateOfMatter.Energy) _tempDeltas[bottomIdx] -= deltaQ / cBottom;
                        }
                    }

                    // Ambient cooling/warming & dissipation (entropy decay towards ambient)
                    if (!selfFixed && TemperatureDecayRate > 0.0f)
                    {
                        float decayFactor = TemperatureDecayRate;
                        if (cell.IsEmpty)
                        {
                            // Air ambient dissipation (gentle cooling/warming to ambient)
                            _tempDeltas[idx] += (amb - cell.Temperature) * (0.035f * decayFactor);
                        }
                        else
                        {
                            float dissRate;
                            if (mat.Definition.State == StateOfMatter.Liquid)
                            {
                                dissRate = (0.0003f + 0.0006f * kSelf) / cSelf;
                            }
                            else if (mat.Definition.State == StateOfMatter.Gas)
                            {
                                dissRate = 0.004f;
                            }
                            else if (mat.Definition.State == StateOfMatter.Energy)
                            {
                                dissRate = 0.0f;
                            }
                            else
                            {
                                dissRate = (0.006f + 0.008f * kSelf) / cSelf;
                            }
                            _tempDeltas[idx] += (amb - cell.Temperature) * (dissRate * decayFactor);
                        }
                    }
                }
            }

            // Apply calculated deltas
            for (int i = 0; i < Grid.Cells.Length; i++)
            {
                ref var cell = ref Grid.Cells[i];
                var mat = reg.GetMaterial(cell.MaterialIndex);
                if (mat.Definition.FixedTemperature)
                {
                    cell.Temperature = mat.Definition.DefaultTemperature;
                }
                else if (mat.Definition.State == StateOfMatter.Energy)
                {
                    cell.Temperature = MathF.Max(mat.Definition.DefaultTemperature, cell.Temperature + _tempDeltas[i]);
                }
                else
                {
                    cell.Temperature = MathF.Max(-273.15f, cell.Temperature + _tempDeltas[i]);
                }
            }
        }

        private void SimulatePhaseChangesAndCombustion()
        {
            int w = Grid.Width;
            int h = Grid.Height;
            var reg = Grid.Registry;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int idx = y * w + x;
                    ref var cell = ref Grid.Cells[idx];

                    if (cell.IsEmpty) continue;

                    var mat = reg.GetMaterial(cell.MaterialIndex);
                    float temp = cell.Temperature;

                    // 1. Explosives check
                    if (mat.Definition.IsExplosive)
                    {
                        if (cell.IsBurning || temp >= mat.Definition.IgnitionTemperature)
                        {
                            _explosionQueue.Enqueue((x, y, cell.MaterialIndex));
                            continue;
                        }
                    }

                    // 2. Flammability ignition
                    if (mat.Definition.IsFlammable && !cell.IsBurning && temp >= mat.Definition.IgnitionTemperature)
                    {
                        cell.SetBurning(true);
                        if (mat.Definition.IsInstantFuse)
                        {
                            cell.Life = 1;
                            PropagateInstantFuse(x, y);
                        }
                        else
                        {
                            int baseFuel = mat.Definition.Fuel;
                            int minFuel = Math.Max(1, (int)(baseFuel * 0.7f));
                            int maxFuel = Math.Max(minFuel + 1, (int)(baseFuel * 1.3f) + 1);
                            cell.Life = (ushort)Grid.Random.Next(minFuel, maxFuel);
                        }
                    }

                    // 3. Melting
                    if (mat.Definition.MeltingPoint.HasValue && temp >= mat.Definition.MeltingPoint.Value && mat.MeltTargetIndex > 0)
                    {
                        Grid.SetCell(x, y, mat.MeltTargetIndex, (float?)temp);
                        continue;
                    }

                    // 4. Freezing
                    if (mat.Definition.FreezingPoint.HasValue && temp < mat.Definition.FreezingPoint.Value && mat.FreezeTargetIndex > 0)
                    {
                        Grid.SetCell(x, y, mat.FreezeTargetIndex, (float?)temp);
                        continue;
                    }

                    // 5. Boiling
                    if (mat.Definition.BoilingPoint.HasValue && temp >= mat.Definition.BoilingPoint.Value && mat.BoilTargetIndex > 0)
                    {
                        float boilPoint = mat.Definition.BoilingPoint.Value;
                        Grid.SetCell(x, y, mat.BoilTargetIndex, (float?)MathF.Min(temp, boilPoint + 5.0f));

                        // Latent heat absorption: boiling vigorously extracts thermal energy from touching hot solids/liquids
                        int[] bx = { 0, 0, 1, -1 };
                        int[] by = { 1, -1, 0, 0 };
                        for (int i = 0; i < 4; i++)
                        {
                            int nx = x + bx[i];
                            int ny = y + by[i];
                            if (Grid.InBounds(nx, ny))
                            {
                                ref var nCell = ref Grid.GetCell(nx, ny);
                                if (!nCell.IsEmpty)
                                {
                                    var nMat = reg.GetMaterial(nCell.MaterialIndex);
                                    if (!nMat.Definition.FixedTemperature && nCell.Temperature > boilPoint)
                                    {
                                        float heatExtraction = MathF.Min(180.0f, (nCell.Temperature - boilPoint) * 0.65f);
                                        nCell.Temperature = MathF.Max(boilPoint, nCell.Temperature - heatExtraction);
                                    }
                                }
                            }
                        }
                        continue;
                    }

                    // 6. Condensation
                    if (mat.Definition.CondensationPoint.HasValue && temp < mat.Definition.CondensationPoint.Value && mat.CondenseTargetIndex > 0)
                    {
                        if (Grid.Random.Next(4) == 0 || temp < mat.Definition.CondensationPoint.Value - 15.0f)
                        {
                            Grid.SetCell(x, y, mat.CondenseTargetIndex, (float?)temp);
                            continue;
                        }
                    }
                }
            }
        }

        public void DetonateAt(int cx, int cy, ushort explosiveMatIndex)
        {
            _explosionQueue.Enqueue((cx, cy, explosiveMatIndex));
            ProcessExplosions();
        }

        private void ProcessExplosions()
        {
            if (_explosionQueue.Count == 0) return;

            var reg = Grid.Registry;
            var rand = Grid.Random;

            while (_explosionQueue.Count > 0)
            {
                var (cx, cy, matIdx) = _explosionQueue.Dequeue();
                var mat = reg.GetMaterial(matIdx);

                int r = mat.Definition.ExplosionRadius > 0 ? mat.Definition.ExplosionRadius : 12;
                float force = mat.Definition.ExplosionForce > 0 ? mat.Definition.ExplosionForce : 10.0f;
                float expTemp = mat.Definition.ExplosionTemperature > 0 ? mat.Definition.ExplosionTemperature : 1500.0f;
                int fireCount = mat.Definition.ExplosionFireCount > 0 ? mat.Definition.ExplosionFireCount : 20;

                ushort fireIdx = reg.GetIndex("fire");
                ushort sparkIdx = reg.GetIndex("spark");
                ushort smokeIdx = reg.GetIndex("smoke");

                // Clear detonation cell
                Grid.SetCell(cx, cy, fireIdx > 0 ? fireIdx : MaterialRegistry.EmptyIndex, (float?)expTemp);

                int r2 = r * r;
                int minX = Math.Max(0, cx - r);
                int maxX = Math.Min(Grid.Width - 1, cx + r);
                int minY = Math.Max(0, cy - r);
                int maxY = Math.Min(Grid.Height - 1, cy + r);

                for (int y = minY; y <= maxY; y++)
                {
                    int dy = y - cy;
                    int dy2 = dy * dy;
                    for (int x = minX; x <= maxX; x++)
                    {
                        int dx = x - cx;
                        int d2 = dx * dx + dy2;

                        if (d2 <= r2)
                        {
                            float dist = (float)Math.Sqrt(d2);
                            float factor = 1.0f - (dist / r);

                            ref var cell = ref Grid.GetCell(x, y);
                            if (cell.IsEmpty)
                            {
                                cell.Temperature += expTemp * factor * 0.5f;
                                continue;
                            }

                            var targetMat = reg.GetMaterial(cell.MaterialIndex);
                            if (targetMat.Definition.FixedTemperature && targetMat.Definition.Hardness > 100.0f)
                            {
                                // Indestructible tool/wall
                                continue;
                            }

                            // Heat deposit
                            cell.Temperature += expTemp * factor;

                            // Chain reaction for explosives
                            if (targetMat.Definition.IsExplosive && (x != cx || y != cy))
                            {
                                _explosionQueue.Enqueue((x, y, cell.MaterialIndex));
                                Grid.SetEmpty(x, y);
                                continue;
                            }

                            // Blast damage / destruction
                            float blastPower = force * factor;
                            if (blastPower >= targetMat.Definition.Hardness)
                            {
                                if (dist < r * 0.35f)
                                {
                                    // Vaporized in core
                                    if (fireIdx > 0 && rand.Next(2) == 0)
                                        Grid.SetCell(x, y, fireIdx, (float?)(expTemp * 0.8f));
                                    else
                                        Grid.SetEmpty(x, y);
                                }
                                else if (dist < r * 0.7f)
                                {
                                    // Burned / disintegrated
                                    if (targetMat.Definition.IsFlammable)
                                    {
                                        if (smokeIdx > 0 && rand.Next(2) == 0)
                                            Grid.SetCell(x, y, smokeIdx, (float?)cell.Temperature);
                                        else
                                            Grid.SetEmpty(x, y);
                                    }
                                    else if (targetMat.Definition.State == StateOfMatter.Solid && targetMat.MeltTargetIndex > 0)
                                    {
                                        // Shatter or melt
                                        Grid.SetCell(x, y, targetMat.MeltTargetIndex, (float?)cell.Temperature);
                                    }
                                    else
                                    {
                                        Grid.SetEmpty(x, y);
                                    }
                                }
                                else
                                {
                                    // Outer shockwave: ignite if flammable
                                    if (targetMat.Definition.IsFlammable && !cell.IsBurning)
                                    {
                                        cell.SetBurning(true);
                                        int baseFuel = targetMat.Definition.Fuel;
                                        int minFuel = Math.Max(1, (int)(baseFuel * 0.7f));
                                        int maxFuel = Math.Max(minFuel + 1, (int)(baseFuel * 1.3f) + 1);
                                        cell.Life = (ushort)rand.Next(minFuel, maxFuel);
                                    }
                                }
                            }
                        }
                    }
                }

                // Spawn flying sparks and fire in blast radius
                for (int i = 0; i < fireCount; i++)
                {
                    double angle = rand.NextDouble() * Math.PI * 2.0;
                    double blastDist = (rand.NextDouble() * 0.85 + 0.15) * r;
                    int fx = cx + (int)(Math.Cos(angle) * blastDist);
                    int fy = cy + (int)(Math.Sin(angle) * blastDist);

                    if (Grid.InBounds(fx, fy))
                    {
                        ref var fCell = ref Grid.GetCell(fx, fy);
                        if (fCell.IsEmpty)
                        {
                            ushort chosen = (rand.Next(3) == 0 && sparkIdx > 0) ? sparkIdx : fireIdx;
                            if (chosen > 0)
                            {
                                Grid.SetCell(fx, fy, chosen, (float?)(expTemp * 0.9f));
                            }
                        }
                    }
                }
            }
        }
    }
}
