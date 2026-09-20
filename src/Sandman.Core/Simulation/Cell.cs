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

    public struct Cell
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
