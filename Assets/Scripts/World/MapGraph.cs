using System.Collections.Generic;
using UnityEngine;
using DNDLLM.Map;

namespace DNDLLM.World
{
    /// <summary>
    /// Tracks the tree of explored map regions so that revisiting a room restores
    /// the previously-generated snapshot instead of calling the LLM again.
    ///
    /// Structure:
    ///   Root node = the top-level campaign map.
    ///   Each door or GM-triggered subregion creates a child node keyed by a
    ///   stable string ("door_X_Y" or "region_<name>").
    ///   Navigation uses a stack so the player can exit back through multiple levels.
    /// </summary>
    public class MapGraph
    {
        // ── Node ─────────────────────────────────────────────────────────────

        public class MapNode
        {
            public string NodeId;
            public string Theme;
            /// <summary>Saved after the map finished generating. Null = never visited / in progress.</summary>
            public MapGenerator.MapSnapshot Snapshot;
            /// <summary>Key → child node id. Keys are "door_X_Y" or "region_name".</summary>
            public readonly Dictionary<string, string> Children = new Dictionary<string, string>();
        }

        // ── State ─────────────────────────────────────────────────────────────

        private readonly Dictionary<string, MapNode> _nodes  = new Dictionary<string, MapNode>();
        private readonly Stack<string>               _navStack = new Stack<string>();
        private string  _currentId;
        private int     _counter;

        // ── Public accessors ──────────────────────────────────────────────────

        public string    CurrentId   => _currentId;
        public bool      CanGoBack   => _navStack.Count > 0;
        public MapNode   CurrentNode => GetNode(_currentId);
        public int       Depth       => _navStack.Count;

        public MapNode GetNode(string id) =>
            id != null && _nodes.TryGetValue(id, out var n) ? n : null;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>Sets up the root node for a fresh campaign.</summary>
        public string InitRoot(string theme)
        {
            _nodes.Clear();
            _navStack.Clear();
            _counter   = 0;
            var root   = new MapNode { NodeId = NextId(), Theme = theme };
            _nodes[root.NodeId] = root;
            _currentId = root.NodeId;
            return _currentId;
        }

        public void Reset()
        {
            _nodes.Clear();
            _navStack.Clear();
            _currentId = null;
            _counter   = 0;
        }

        // ── Snapshot storage ──────────────────────────────────────────────────

        /// <summary>Saves (or overwrites) the snapshot for the currently-active node.</summary>
        public void SaveCurrentSnapshot(MapGenerator.MapSnapshot snap)
        {
            if (CurrentNode != null) CurrentNode.Snapshot = snap;
        }

        // ── Child lookup / creation ───────────────────────────────────────────

        /// <summary>
        /// Gets or creates the child node for a Door tile at (x, y).
        /// Returns (nodeId, isNew). isNew == false means we have a cached snapshot.
        /// </summary>
        public (string nodeId, bool isNew) GetOrCreateDoorChild(int doorX, int doorY, string childTheme)
            => GetOrCreateChild($"door_{doorX}_{doorY}", childTheme);

        /// <summary>
        /// Gets or creates the child node for a GM-issued ENTER_SUBREGION command.
        /// The region name is normalised as the stable key so the same subregion
        /// name from the same parent room always resolves to the same node.
        /// </summary>
        public (string nodeId, bool isNew) GetOrCreateRegionChild(string regionName, string childTheme)
            => GetOrCreateChild($"region_{regionName.ToLower().Trim().Replace(" ", "_")}", childTheme);

        private (string nodeId, bool isNew) GetOrCreateChild(string key, string childTheme)
        {
            var current = CurrentNode;
            if (current == null)
            {
                // Degenerate: no root — create orphan
                string orphan = NextId();
                _nodes[orphan] = new MapNode { NodeId = orphan, Theme = childTheme };
                return (orphan, true);
            }

            if (current.Children.TryGetValue(key, out string existingId))
                return (existingId, false);

            var child = new MapNode { NodeId = NextId(), Theme = childTheme };
            _nodes[child.NodeId]  = child;
            current.Children[key] = child.NodeId;
            Debug.Log($"[MapGraph] New node '{child.NodeId}' ({childTheme}) under '{_currentId}' via '{key}'");
            return (child.NodeId, true);
        }

        // ── Navigation ────────────────────────────────────────────────────────

        public void NavigateTo(string childId)
        {
            _navStack.Push(_currentId);
            _currentId = childId;
            Debug.Log($"[MapGraph] Navigated to '{_currentId}' (depth {_navStack.Count})");
        }

        /// <summary>Pops back to the parent node. Returns the parent id (new current).</summary>
        public string NavigateBack()
        {
            if (_navStack.Count > 0)
                _currentId = _navStack.Pop();
            Debug.Log($"[MapGraph] Returned to '{_currentId}' (depth {_navStack.Count})");
            return _currentId;
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private string NextId() => $"map_{++_counter}";
    }
}
