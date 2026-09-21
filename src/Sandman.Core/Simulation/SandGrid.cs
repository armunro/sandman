using System;
using System.Runtime.CompilerServices;
using Sandman.Core.Config;
using Sandman.Core.Models;

namespace Sandman.Core.Simulation
{
    public class SandGrid
    {
        public int Width { get; }
        public int Height { get; }
        public MaterialRegistry Registry { get; }
        public Random Random { get; }

        public Cell[] Cells { get; }
        public float AmbientTemperature { get; set; } = 20.0f;

        public void SetAmbientTemperature(float temperature, bool updateAirCells = false)
        {
            AmbientTemperature = temperature;
            if (updateAirCells)
            {
                for (int i = 0; i < Cells.Length; i++)
                {
                    if (Cells[i].IsEmpty)
                    {
                        Cells[i].Temperature = temperature;
                    }
                }
            }
        }

        public SandGrid(int width, int height, MaterialRegistry registry, int? seed = null)
        {
            Width = width;
            Height = height;
            Registry = registry;
            Random = seed.HasValue ? new Random(seed.Value) : new Random();
            Cells = new Cell[width * height];

            Clear();
        }

        public GridSnapshot CreateSnapshot() => new GridSnapshot(this);

        public void RestoreSnapshot(GridSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            snapshot.RestoreTo(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetIndex(int x, int y) => y * Width + x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool InBounds(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref Cell GetCell(int x, int y) => ref Cells[y * Width + x];

        public void Clear()
        {
            for (int i = 0; i < Cells.Length; i++)
            {
                Cells[i] = new Cell
                {
                    MaterialIndex = MaterialRegistry.EmptyIndex,
                    Temperature = AmbientTemperature,
                    Life = 0,
                    Flags = CellFlags.None,
                    Color = 0xFF000000,
                    VelocityX = 0,
                    VelocityY = 0
                };
            }
        }

        public void SetCell(int x, int y, ushort materialIndex, float? temperature = null, ushort? life = null)
        {
            if (!InBounds(x, y)) return;

            var mat = Registry.GetMaterial(materialIndex);
            float temp = temperature ?? mat.Definition.DefaultTemperature;
            uint color = mat.GenerateColor(Random);
            ushort cellLife;
            if (life.HasValue)
            {
                cellLife = life.Value;
            }
            else if (mat.Definition.Lifetime > 0)
            {
                int baseLife = mat.Definition.Lifetime;
                int minLife = Math.Max(1, (int)(baseLife * 0.6f));
                int maxLife = Math.Max(minLife + 1, (int)(baseLife * 1.4f) + 1);
                cellLife = (ushort)Random.Next(minLife, maxLife);
            }
            else
            {
                cellLife = 0;
            }

            ref var cell = ref GetCell(x, y);
            cell.MaterialIndex = materialIndex;
            cell.Temperature = temp;
            cell.Life = cellLife;
            cell.Flags = CellFlags.None;
            cell.Color = color;
            cell.VelocityX = 0;
            cell.VelocityY = 0;
        }

        public void SetCellById(int x, int y, string materialId, float? temperature = null)
        {
            ushort idx = Registry.GetIndex(materialId);
            SetCell(x, y, idx, temperature);
        }

        public void SetEmpty(int x, int y)
        {
            if (!InBounds(x, y)) return;

            ref var cell = ref GetCell(x, y);
            cell.MaterialIndex = MaterialRegistry.EmptyIndex;
            cell.Temperature = AmbientTemperature;
            cell.Life = 0;
            cell.Flags = CellFlags.None;
            cell.Color = 0xFF000000;
            cell.VelocityX = 0;
            cell.VelocityY = 0;
        }

        public void DrawCircle(int cx, int cy, int radius, ushort materialIndex, float? temperature = null, bool overwrite = true, float sprayDensity = 1.0f)
        {
            int r2 = radius * radius;
            int minX = Math.Max(0, cx - radius);
            int maxX = Math.Min(Width - 1, cx + radius);
            int minY = Math.Max(0, cy - radius);
            int maxY = Math.Min(Height - 1, cy + radius);

            for (int y = minY; y <= maxY; y++)
            {
                int dy = y - cy;
                int dy2 = dy * dy;
                for (int x = minX; x <= maxX; x++)
                {
                    int dx = x - cx;
                    if (dx * dx + dy2 <= r2)
                    {
                        if (sprayDensity < 1.0f && Random.NextDouble() > sprayDensity)
                            continue;

                        ref var cell = ref GetCell(x, y);
                        if (overwrite || cell.IsEmpty)
                        {
                            SetCell(x, y, materialIndex, temperature);
                        }
                    }
                }
            }
        }

        public void DrawSquare(int cx, int cy, int size, ushort materialIndex, float? temperature = null, bool overwrite = true)
        {
            int half = size / 2;
            int minX = Math.Max(0, cx - half);
            int maxX = Math.Min(Width - 1, cx + half);
            int minY = Math.Max(0, cy - half);
            int maxY = Math.Min(Height - 1, cy + half);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    ref var cell = ref GetCell(x, y);
                    if (overwrite || cell.IsEmpty)
                    {
                        SetCell(x, y, materialIndex, temperature);
                    }
                }
            }
        }

        public void DrawLine(int x0, int y0, int x1, int y1, int radius, ushort materialIndex, float? temperature = null, bool overwrite = true)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (radius <= 1)
                {
                    if (InBounds(x0, y0))
                    {
                        ref var cell = ref GetCell(x0, y0);
                        if (overwrite || cell.IsEmpty)
                            SetCell(x0, y0, materialIndex, temperature);
                    }
                }
                else
                {
                    DrawCircle(x0, y0, radius, materialIndex, temperature, overwrite);
                }

                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        public void DrawBox(int x0, int y0, int x1, int y1, ushort materialIndex, float? temperature = null, bool filled = false, int thickness = 1)
        {
            int minX = Math.Clamp(Math.Min(x0, x1), 0, Width - 1);
            int maxX = Math.Clamp(Math.Max(x0, x1), 0, Width - 1);
            int minY = Math.Clamp(Math.Min(y0, y1), 0, Height - 1);
            int maxY = Math.Clamp(Math.Max(y0, y1), 0, Height - 1);

            int t = Math.Max(1, thickness);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (filled || x < minX + t || x > maxX - t || y < minY + t || y > maxY - t)
                    {
                        SetCell(x, y, materialIndex, temperature);
                    }
                }
            }
        }

        public void DrawEllipse(int x0, int y0, int x1, int y1, ushort materialIndex, float? temperature = null, bool filled = false, int thickness = 1)
        {
            int minX = Math.Min(x0, x1);
            int maxX = Math.Max(x0, x1);
            int minY = Math.Min(y0, y1);
            int maxY = Math.Max(y0, y1);

            float cx = (minX + maxX) / 2.0f;
            float cy = (minY + maxY) / 2.0f;
            float rx = Math.Max(0.5f, (maxX - minX) / 2.0f);
            float ry = Math.Max(0.5f, (maxY - minY) / 2.0f);

            int startX = Math.Clamp(minX, 0, Width - 1);
            int endX = Math.Clamp(maxX, 0, Width - 1);
            int startY = Math.Clamp(minY, 0, Height - 1);
            int endY = Math.Clamp(maxY, 0, Height - 1);

            int t = Math.Max(1, thickness);
            float rxInner = rx - t;
            float ryInner = ry - t;

            for (int y = startY; y <= endY; y++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    float dx = (x - cx) / rx;
                    float dy = (y - cy) / ry;
                    float distSq = dx * dx + dy * dy;

                    if (distSq <= 1.0f)
                    {
                        if (filled || rxInner <= 0.0f || ryInner <= 0.0f)
                        {
                            SetCell(x, y, materialIndex, temperature);
                        }
                        else
                        {
                            float dxIn = (x - cx) / rxInner;
                            float dyIn = (y - cy) / ryInner;
                            if (dxIn * dxIn + dyIn * dyIn >= 1.0f)
                            {
                                SetCell(x, y, materialIndex, temperature);
                            }
                        }
                    }
                }
            }
        }

        public void FloodFill(int startX, int startY, ushort newMaterialIndex, float? temperature = null)
        {
            if (!InBounds(startX, startY)) return;

            ushort targetMaterial = GetCell(startX, startY).MaterialIndex;
            if (targetMaterial == newMaterialIndex) return;

            var queue = new System.Collections.Generic.Queue<(int X, int Y)>();
            var visited = new bool[Width * Height];

            queue.Enqueue((startX, startY));
            visited[GetIndex(startX, startY)] = true;

            while (queue.Count > 0)
            {
                var (x, y) = queue.Dequeue();
                SetCell(x, y, newMaterialIndex, temperature);

                int[] dx = { 0, 0, 1, -1 };
                int[] dy = { 1, -1, 0, 0 };

                for (int i = 0; i < 4; i++)
                {
                    int nx = x + dx[i];
                    int ny = y + dy[i];

                    if (InBounds(nx, ny))
                    {
                        int nIdx = GetIndex(nx, ny);
                        if (!visited[nIdx] && Cells[nIdx].MaterialIndex == targetMaterial)
                        {
                            visited[nIdx] = true;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
            }
        }

        public void ApplyThermalBrush(int cx, int cy, int radius, float deltaTemp)
        {
            int r2 = radius * radius;
            int minX = Math.Max(0, cx - radius);
            int maxX = Math.Min(Width - 1, cx + radius);
            int minY = Math.Max(0, cy - radius);
            int maxY = Math.Min(Height - 1, cy + radius);

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
                        float factor = 1.0f - (float)Math.Sqrt(d2) / radius;
                        ref var cell = ref GetCell(x, y);
                        cell.Temperature += deltaTemp * factor;
                    }
                }
            }
        }

        public int CountActiveParticles()
        {
            int count = 0;
            for (int i = 0; i < Cells.Length; i++)
            {
                if (Cells[i].MaterialIndex != MaterialRegistry.EmptyIndex)
                    count++;
            }
            return count;
        }

        public void RenderToPixelBuffer(int[] pixelBuffer, ViewMode mode = ViewMode.Normal)
        {
            if (pixelBuffer.Length != Cells.Length) return;

            if (mode == ViewMode.Normal)
            {
                for (int i = 0; i < Cells.Length; i++)
                {
                    ref var cell = ref Cells[i];
                    if (cell.IsEmpty)
                    {
                        pixelBuffer[i] = unchecked((int)0xFF141419); // Dark background
                    }
                    else
                    {
                        // Thermal incandescence / glow at warm & high temperatures (e.g. above 150°C)
                        uint c = cell.Color;
                        if (cell.Temperature > 150.0f)
                        {
                            c = ApplyHeatGlow(c, cell.Temperature);
                        }
                        pixelBuffer[i] = unchecked((int)c);
                    }
                }
            }
            else if (mode == ViewMode.ThermalHeatmap)
            {
                for (int i = 0; i < Cells.Length; i++)
                {
                    ref var cell = ref Cells[i];
                    pixelBuffer[i] = unchecked((int)GetHeatmapColor(cell.Temperature));
                }
            }
            else if (mode == ViewMode.StateOfMatter)
            {
                for (int i = 0; i < Cells.Length; i++)
                {
                    ref var cell = ref Cells[i];
                    if (cell.IsEmpty)
                    {
                        pixelBuffer[i] = unchecked((int)0xFF141419);
                    }
                    else
                    {
                        var mat = Registry.GetMaterial(cell.MaterialIndex);
                        uint stateColor = mat.Definition.State switch
                        {
                            StateOfMatter.Solid => 0xFF888888,
                            StateOfMatter.MovableSolid => 0xFFE0C060,
                            StateOfMatter.Liquid => 0xFF3080FF,
                            StateOfMatter.Gas => 0xFFE0E0FF,
                            StateOfMatter.Energy => 0xFFFF4020,
                            _ => 0xFFFFFFFF
                        };
                        pixelBuffer[i] = unchecked((int)stateColor);
                    }
                }
            }
        }

        private static uint ApplyHeatGlow(uint baseColor, float temperature)
        {
            byte a = (byte)((baseColor >> 24) & 0xFF);
            byte r = (byte)((baseColor >> 16) & 0xFF);
            byte g = (byte)((baseColor >> 8) & 0xFF);
            byte b = (byte)(baseColor & 0xFF);

            float glowRatio = Math.Clamp((temperature - 150.0f) / 1400.0f, 0.0f, 1.0f);
            byte gr = (byte)Math.Min(255, r + (int)(glowRatio * 220));
            byte gg = (byte)Math.Min(255, g + (int)(glowRatio * 130));
            byte gb = (byte)Math.Min(255, b + (int)(glowRatio * 60));

            return (uint)((a << 24) | (gr << 16) | (gg << 8) | gb);
        }

        public static uint GetHeatmapColor(float temp)
        {
            // Range: -200°C to 2000°C
            // Cold: Blue -> Cyan -> Green -> Yellow -> Red -> White (Hot)
            float t = Math.Clamp((temp + 200.0f) / 2200.0f, 0.0f, 1.0f);

            byte r, g, b;
            if (t < 0.2f) // Deep blue to cyan
            {
                float local = t / 0.2f;
                r = 0;
                g = (byte)(local * 255);
                b = 255;
            }
            else if (t < 0.4f) // Cyan to green
            {
                float local = (t - 0.2f) / 0.2f;
                r = 0;
                g = 255;
                b = (byte)((1.0f - local) * 255);
            }
            else if (t < 0.6f) // Green to yellow
            {
                float local = (t - 0.4f) / 0.2f;
                r = (byte)(local * 255);
                g = 255;
                b = 0;
            }
            else if (t < 0.8f) // Yellow to red
            {
                float local = (t - 0.6f) / 0.2f;
                r = 255;
                g = (byte)((1.0f - local) * 255);
                b = 0;
            }
            else // Red to white
            {
                float local = (t - 0.8f) / 0.2f;
                r = 255;
                g = (byte)(local * 255);
                b = (byte)(local * 255);
            }

            return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
        }
    }

    public enum ViewMode
    {
        Normal,
        ThermalHeatmap,
        StateOfMatter
    }
}
