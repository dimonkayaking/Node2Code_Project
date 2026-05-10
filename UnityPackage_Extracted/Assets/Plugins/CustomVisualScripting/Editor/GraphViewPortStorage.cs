using System;
using GraphProcessor;
using UnityEditor.Experimental.GraphView;
using VisualScripting.Core.Models;

namespace CustomVisualScripting.Editor
{
    /// <summary>
    /// Canonical port ids for persistence (fieldName / displayName / exec fallbacks).
    /// </summary>
    public static class GraphViewPortStorage
    {
        public static string CanonicalPortIdForStorage(PortView port)
        {
            var fn = NormalizePortId(port.fieldName);
            if (!string.IsNullOrEmpty(fn))
                return fn;

            var pn = NormalizePortId(port.portName);
            if (!string.IsNullOrEmpty(pn))
                return pn;

            if (port.direction == Direction.Input)
                return PortIds.ExecIn;
            if (port.direction == Direction.Output)
                return PortIds.ExecOut;

            return "";
        }

        public static bool IsPortMatchForStorage(PortView port, string savedPortId)
        {
            if (port == null || string.IsNullOrWhiteSpace(savedPortId))
                return false;

            var expected = NormalizePortId(savedPortId);
            if (string.IsNullOrEmpty(expected))
                return false;

            var field = NormalizePortId(port.fieldName);
            if (!string.IsNullOrEmpty(field) &&
                string.Equals(field, expected, StringComparison.OrdinalIgnoreCase))
                return true;

            var name = NormalizePortId(port.portName);
            return !string.IsNullOrEmpty(name) &&
                   string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePortId(string rawPortId)
        {
            return PortIds.Normalize(rawPortId);
        }
    }
}
