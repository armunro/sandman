using System;
using Sandman.Core.Config;
using Sandman.Core.Models;
using Sandman.Core.Simulation;

namespace Sandman.App
{
    public static class DemoScenes
    {
        public static void LoadVolcano(SandGrid grid)
        {
            grid.Clear();
            int w = grid.Width;
            int h = grid.Height;
            ushort basaltIdx = grid.Registry.GetIndex("basalt");
            ushort lavaIdx = grid.Registry.GetIndex("lava");
            ushort waterIdx = grid.Registry.GetIndex("water");
            ushort iceIdx = grid.Registry.GetIndex("ice");
            ushort plantIdx = grid.Registry.GetIndex("plant");
            ushort lavaSpout = grid.Registry.GetIndex("lava_spout");

            // Build mountain slope with basalt
            for (int x = 0; x < w; x++)
            {
                int mountainY = (int)(h * 0.7 - Math.Exp(-Math.Pow((x - w / 2) / 35.0, 2)) * (h * 0.45));
                grid.DrawLine(x, mountainY, x, h - 1, 2, basaltIdx);
            }

            // Volcano caldera crater
            int craterX = w / 2;
            int craterY = (int)(h * 0.28);
            grid.DrawCircle(craterX, craterY, 12, MaterialRegistry.EmptyIndex);

            // Lava spout in crater
            grid.SetCell(craterX, craterY + 6, lavaSpout > 0 ? lavaSpout : lavaIdx, 1300.0f);
            grid.DrawCircle(craterX, craterY + 2, 8, lavaIdx, 1200.0f);

            // Water lake on right
            grid.DrawBox((int)(w * 0.75), (int)(h * 0.75), w - 5, h - 2, waterIdx, filled: true);

            // Ice cap on mountain side
            grid.DrawBox(20, (int)(h * 0.55), 50, (int)(h * 0.62), iceIdx, temperature: -15.0f, filled: true);

            // Vegetation on foothills
            grid.DrawBox(5, (int)(h * 0.78), 45, (int)(h * 0.82), plantIdx, filled: true);
        }

        public static void LoadExplosivesShowcase(SandGrid grid)
        {
            grid.Clear();
            int w = grid.Width;
            int h = grid.Height;

            ushort wallIdx = grid.Registry.GetIndex("wall");
            ushort woodIdx = grid.Registry.GetIndex("wood");
            ushort gunpowderIdx = grid.Registry.GetIndex("gunpowder");
            ushort tntIdx = grid.Registry.GetIndex("tnt");
            ushort c4Idx = grid.Registry.GetIndex("c4");
            ushort dynamiteIdx = grid.Registry.GetIndex("dynamite");
            ushort fireworksIdx = grid.Registry.GetIndex("fireworks");
            ushort sparkIdx = grid.Registry.GetIndex("spark");

            // Ground
            grid.DrawBox(0, h - 10, w - 1, h - 1, wallIdx, filled: true);

            // Wood house / building
            int houseX = w / 2 - 40;
            int houseY = h - 70;
            grid.DrawBox(houseX, houseY, houseX + 80, h - 11, woodIdx, filled: false);
            grid.DrawLine(houseX, houseY, houseX + 40, houseY - 25, 2, woodIdx);
            grid.DrawLine(houseX + 40, houseY - 25, houseX + 80, houseY, 2, woodIdx);

            // TNT and C4 caches inside building
            grid.DrawBox(houseX + 15, h - 25, houseX + 35, h - 12, tntIdx, filled: true);
            grid.DrawBox(houseX + 45, h - 25, houseX + 65, h - 12, c4Idx, filled: true);

            // Gunpowder fuse line leading outside
            grid.DrawLine(houseX + 25, h - 26, houseX - 30, h - 11, 2, gunpowderIdx);
            grid.DrawBox(houseX - 45, h - 22, houseX - 30, h - 12, dynamiteIdx, filled: true);

            // Fireworks mortars on the right
            grid.DrawBox(w - 50, h - 35, w - 30, h - 12, fireworksIdx, filled: true);

            // A lit spark on the gunpowder fuse
            grid.SetCell(houseX - 30, h - 12, sparkIdx, 1000.0f);
        }

        public static void LoadMetallurgyLab(SandGrid grid)
        {
            grid.Clear();
            int w = grid.Width;
            int h = grid.Height;

            ushort wallIdx = grid.Registry.GetIndex("wall");
            ushort ironIdx = grid.Registry.GetIndex("iron");
            ushort copperIdx = grid.Registry.GetIndex("copper");
            ushort goldIdx = grid.Registry.GetIndex("gold");
            ushort plasticIdx = grid.Registry.GetIndex("plastic");
            ushort heaterIdx = grid.Registry.GetIndex("heater");
            ushort coolerIdx = grid.Registry.GetIndex("cooler");

            // Base platform
            grid.DrawBox(0, h - 10, w - 1, h - 1, wallIdx, filled: true);

            // Crucible 1: Iron with strong heater
            int c1X = 30;
            int cY = h - 50;
            grid.DrawBox(c1X, cY, c1X + 30, cY + 30, wallIdx, filled: false);
            grid.DrawBox(c1X + 1, cY + 28, c1X + 29, cY + 29, heaterIdx, temperature: 1800.0f, filled: true);
            grid.DrawBox(c1X + 5, cY + 10, c1X + 25, cY + 25, ironIdx, temperature: 1550.0f, filled: true);

            // Crucible 2: Copper
            int c2X = 80;
            grid.DrawBox(c2X, cY, c2X + 30, cY + 30, wallIdx, filled: false);
            grid.DrawBox(c2X + 1, cY + 28, c2X + 29, cY + 29, heaterIdx, temperature: 1300.0f, filled: true);
            grid.DrawBox(c2X + 5, cY + 10, c2X + 25, cY + 25, copperIdx, temperature: 1100.0f, filled: true);

            // Crucible 3: Gold
            int c3X = 130;
            grid.DrawBox(c3X, cY, c3X + 30, cY + 30, wallIdx, filled: false);
            grid.DrawBox(c3X + 1, cY + 28, c3X + 29, cY + 29, heaterIdx, temperature: 1200.0f, filled: true);
            grid.DrawBox(c3X + 5, cY + 10, c3X + 25, cY + 25, goldIdx, temperature: 1080.0f, filled: true);

            // Cooling chamber on right
            int c4X = 180;
            grid.DrawBox(c4X, cY, c4X + 40, cY + 30, wallIdx, filled: false);
            grid.DrawBox(c4X + 1, cY + 28, c4X + 39, cY + 29, coolerIdx, temperature: -80.0f, filled: true);
            grid.DrawBox(c4X + 5, cY + 10, c4X + 35, cY + 25, plasticIdx, temperature: 20.0f, filled: true);
        }

        public static void LoadFluidDynamics(SandGrid grid)
        {
            grid.Clear();
            int w = grid.Width;
            int h = grid.Height;

            ushort wallIdx = grid.Registry.GetIndex("wall");
            ushort waterIdx = grid.Registry.GetIndex("water");
            ushort oilIdx = grid.Registry.GetIndex("oil");
            ushort acidIdx = grid.Registry.GetIndex("acid");
            ushort sandIdx = grid.Registry.GetIndex("sand");

            // Container
            grid.DrawBox(20, 20, w - 20, h - 10, wallIdx, filled: false);

            // Three separate stratified layers
            int top = 50;
            int bottom = h - 12;
            int seg = (bottom - top) / 4;

            // Sand at bottom
            grid.DrawBox(22, bottom - seg, w - 22, bottom, sandIdx, filled: true);
            // Acid above sand
            grid.DrawBox(22, bottom - 2 * seg, w - 22, bottom - seg - 1, acidIdx, filled: true);
            // Water above acid
            grid.DrawBox(22, bottom - 3 * seg, w - 22, bottom - 2 * seg - 1, waterIdx, filled: true);
            // Oil on top (lightest liquid)
            grid.DrawBox(22, top, w - 22, bottom - 3 * seg - 1, oilIdx, filled: true);
        }

        public static void LoadBinaryExplosivesDemo(SandGrid grid)
        {
            grid.Clear();
            int w = grid.Width;
            int h = grid.Height;

            ushort wallIdx = grid.Registry.GetIndex("wall");
            ushort woodIdx = grid.Registry.GetIndex("wood");
            ushort stoneIdx = grid.Registry.GetIndex("stone");
            ushort bexpA = grid.Registry.GetIndex("binary_explosive_a");
            ushort bexpB = grid.Registry.GetIndex("binary_explosive_b");

            // Ground and target structure below
            grid.DrawBox(0, h - 10, w - 1, h - 1, wallIdx, filled: true);
            grid.DrawBox(w / 2 - 25, h - 45, w / 2 + 25, h - 11, woodIdx, filled: false);
            grid.DrawBox(w / 2 - 15, h - 30, w / 2 + 15, h - 11, stoneIdx, filled: true);

            // Funnel 1 on left for Binary Explosive A
            int f1X = w / 2 - 40;
            grid.DrawLine(f1X - 30, 20, f1X, 60, 2, wallIdx);
            grid.DrawLine(f1X - 30, 20, f1X - 30, 10, 2, wallIdx);
            grid.DrawLine(f1X + 15, 20, f1X + 8, 60, 2, wallIdx);
            grid.DrawLine(f1X + 15, 20, f1X + 15, 10, 2, wallIdx);
            grid.DrawBox(f1X - 25, 12, f1X + 10, 35, bexpA, filled: true);

            // Funnel 2 on right for Binary Explosive B
            int f2X = w / 2 + 40;
            grid.DrawLine(f2X - 15, 20, f2X - 8, 60, 2, wallIdx);
            grid.DrawLine(f2X - 15, 20, f2X - 15, 10, 2, wallIdx);
            grid.DrawLine(f2X + 30, 20, f2X, 60, 2, wallIdx);
            grid.DrawLine(f2X + 30, 20, f2X + 30, 10, 2, wallIdx);
            grid.DrawBox(f2X - 10, 12, f2X + 25, 35, bexpB, filled: true);

            // Converging chute guiding them together into the mixing zone
            grid.DrawLine(f1X, 60, w / 2 - 6, 85, 2, wallIdx);
            grid.DrawLine(f2X, 60, w / 2 + 6, 85, 2, wallIdx);
        }

        public static void LoadFusesDemo(SandGrid grid)
        {
            grid.Clear();
            int w = grid.Width;
            int h = grid.Height;

            ushort wallIdx = grid.Registry.GetIndex("wall");
            ushort sparkIdx = grid.Registry.GetIndex("spark");
            ushort slowFuseIdx = grid.Registry.GetIndex("slow_fuse");
            ushort stdFuseIdx = grid.Registry.GetIndex("fuse");
            ushort fastFuseIdx = grid.Registry.GetIndex("fast_fuse");
            ushort instantFuseIdx = grid.Registry.GetIndex("instant_fuse");
            ushort waterproofFuseIdx = grid.Registry.GetIndex("waterproof_fuse");
            ushort waterIdx = grid.Registry.GetIndex("water");
            ushort fireworksIdx = grid.Registry.GetIndex("fireworks");
            ushort dynamiteIdx = grid.Registry.GetIndex("dynamite");
            ushort tntIdx = grid.Registry.GetIndex("tnt");
            ushort c4Idx = grid.Registry.GetIndex("c4");

            // Base platform
            grid.DrawBox(0, h - 8, w - 1, h - 1, wallIdx, filled: true);

            int startX = 25;
            int endX = w - 45;

            // Lane 1: Instant Fuse (Flash/DetCord) -> TNT
            int y1 = 30;
            grid.DrawLine(startX, y1, endX, y1, 2, instantFuseIdx);
            grid.DrawBox(endX, y1 - 4, endX + 16, y1 + 4, tntIdx, filled: true);

            // Lane 2: Fast Fuse -> Fireworks
            int y2 = 60;
            grid.DrawLine(startX, y2, endX, y2, 2, fastFuseIdx);
            grid.DrawBox(endX, y2 - 4, endX + 16, y2 + 4, fireworksIdx, filled: true);

            // Lane 3: Standard Fuse -> C4
            int y3 = 90;
            grid.DrawLine(startX, y3, endX, y3, 2, stdFuseIdx);
            grid.DrawBox(endX, y3 - 4, endX + 16, y3 + 4, c4Idx, filled: true);

            // Lane 4: Slow Delay Fuse -> Dynamite
            int y4 = 120;
            grid.DrawLine(startX, y4, endX, y4, 2, slowFuseIdx);
            grid.DrawBox(endX, y4 - 4, endX + 16, y4 + 4, dynamiteIdx, filled: true);

            // Lane 5: Waterproof Fuse through a water tank -> Fireworks
            int y5 = 150;
            grid.DrawBox(startX + 40, y5 - 8, startX + 90, y5 + 8, wallIdx, filled: false);
            grid.DrawBox(startX + 41, y5 - 7, startX + 89, y5 + 7, waterIdx, filled: true);
            grid.DrawLine(startX, y5, endX, y5, 2, waterproofFuseIdx);
            grid.DrawBox(endX, y5 - 4, endX + 16, y5 + 4, fireworksIdx, filled: true);

            // Ignition bus line on left connecting all fuse lanes
            grid.DrawLine(startX, y1, startX, y5, 2, instantFuseIdx);

            // Ignite the trigger
            grid.SetCell(startX, (y1 + y2) / 2, sparkIdx, 1000.0f);
        }
    }
}
