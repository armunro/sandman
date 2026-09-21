using System;

namespace Sandman.Core.Simulation
{
    /// <summary>
    /// Represents an immutable point-in-time snapshot of a SandGrid's cell states and settings.
    /// </summary>
    public class GridSnapshot
    {
        public int Width { get; }
        public int Height { get; }
        public float AmbientTemperature { get; }
        public Cell[] Cells { get; }

        public GridSnapshot(int width, int height, float ambientTemperature, ReadOnlySpan<Cell> cells)
        {
            Width = width;
            Height = height;
            AmbientTemperature = ambientTemperature;
            Cells = cells.ToArray();
        }

        public GridSnapshot(SandGrid grid)
        {
            ArgumentNullException.ThrowIfNull(grid);
            Width = grid.Width;
            Height = grid.Height;
            AmbientTemperature = grid.AmbientTemperature;
            Cells = (Cell[])grid.Cells.Clone();
        }

        /// <summary>
        /// Restores this snapshot onto the target SandGrid.
        /// </summary>
        public void RestoreTo(SandGrid grid)
        {
            ArgumentNullException.ThrowIfNull(grid);
            if (grid.Width != Width || grid.Height != Height)
            {
                throw new InvalidOperationException($"Snapshot dimensions ({Width}x{Height}) do not match grid dimensions ({grid.Width}x{grid.Height}).");
            }

            Array.Copy(Cells, grid.Cells, Cells.Length);
            grid.AmbientTemperature = AmbientTemperature;
        }

        /// <summary>
        /// Compares this snapshot with a SandGrid to determine if there are differences.
        /// </summary>
        public bool HasDifferences(SandGrid grid)
        {
            if (grid == null) return true;
            if (grid.Width != Width || grid.Height != Height) return true;
            if (Math.Abs(grid.AmbientTemperature - AmbientTemperature) > 0.001f) return true;

            var gridCells = grid.Cells;
            for (int i = 0; i < Cells.Length; i++)
            {
                if (!Cells[i].Equals(gridCells[i]))
                    return true;
            }

            return false;
        }
    }
}
