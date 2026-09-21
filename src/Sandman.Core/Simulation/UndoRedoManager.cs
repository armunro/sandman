using System;
using System.Collections.Generic;

namespace Sandman.Core.Simulation
{
    /// <summary>
    /// Manages Undo and Redo operations using grid snapshots.
    /// </summary>
    public class UndoRedoManager
    {
        private readonly LinkedList<GridSnapshot> _undoStack = new();
        private readonly LinkedList<GridSnapshot> _redoStack = new();
        private int _maxHistory = 50;

        public int MaxHistory
        {
            get => _maxHistory;
            set
            {
                _maxHistory = Math.Max(1, value);
                TrimHistory();
            }
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;

        public event EventHandler? HistoryChanged;

        public void PushSnapshot(GridSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);
            _undoStack.AddLast(snapshot);
            TrimHistory();
            _redoStack.Clear();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RecordBeforeChange(SandGrid grid)
        {
            ArgumentNullException.ThrowIfNull(grid);
            PushSnapshot(grid.CreateSnapshot());
        }

        public bool Undo(SandGrid grid)
        {
            ArgumentNullException.ThrowIfNull(grid);
            if (!CanUndo) return false;

            var currentState = grid.CreateSnapshot();
            _redoStack.AddLast(currentState);
            while (_redoStack.Count > _maxHistory)
            {
                _redoStack.RemoveFirst();
            }

            var previousState = _undoStack.Last!.Value;
            _undoStack.RemoveLast();

            previousState.RestoreTo(grid);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool Redo(SandGrid grid)
        {
            ArgumentNullException.ThrowIfNull(grid);
            if (!CanRedo) return false;

            var currentState = grid.CreateSnapshot();
            _undoStack.AddLast(currentState);
            while (_undoStack.Count > _maxHistory)
            {
                _undoStack.RemoveFirst();
            }

            var nextState = _redoStack.Last!.Value;
            _redoStack.RemoveLast();

            nextState.RestoreTo(grid);
            HistoryChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            HistoryChanged?.Invoke(this, EventArgs.Empty);
        }

        private void TrimHistory()
        {
            while (_undoStack.Count > _maxHistory)
            {
                _undoStack.RemoveFirst();
            }
            while (_redoStack.Count > _maxHistory)
            {
                _redoStack.RemoveFirst();
            }
        }
    }
}
