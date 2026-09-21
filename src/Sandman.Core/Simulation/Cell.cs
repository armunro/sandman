using System;

namespace Sandman.Core.Simulation
{
    [Flags]
    public enum CellFlags : byte
    {
        None = 0,
        Burning = 1 << 0,
        Updated = 1 << 1
    }

    public struct Cell : IEquatable<Cell>
    {
        public ushort MaterialIndex;
        public float Temperature;
        public ushort Life;
        public CellFlags Flags;
        public uint Color;
        public sbyte VelocityX;
        public sbyte VelocityY;

        public bool IsEmpty => MaterialIndex == 0;
        public bool IsBurning => (Flags & CellFlags.Burning) != 0;
        public bool HasUpdated => (Flags & CellFlags.Updated) != 0;

        public readonly bool Equals(Cell other)
        {
            return MaterialIndex == other.MaterialIndex &&
                   Math.Abs(Temperature - other.Temperature) < 0.001f &&
                   Life == other.Life &&
                   Flags == other.Flags &&
                   Color == other.Color &&
                   VelocityX == other.VelocityX &&
                   VelocityY == other.VelocityY;
        }

        public override readonly bool Equals(object? obj) => obj is Cell other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(MaterialIndex, Temperature, Life, Flags, Color, VelocityX, VelocityY);

        public static bool operator ==(Cell left, Cell right) => left.Equals(right);
        public static bool operator !=(Cell left, Cell right) => !left.Equals(right);

        public void SetBurning(bool burning)
        {
            if (burning)
                Flags |= CellFlags.Burning;
            else
                Flags &= ~CellFlags.Burning;
        }

        public void SetUpdated(bool updated)
        {
            if (updated)
                Flags |= CellFlags.Updated;
            else
                Flags &= ~CellFlags.Updated;
        }
    }
}
