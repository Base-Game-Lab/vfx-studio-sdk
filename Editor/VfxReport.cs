using System.Collections.Generic;
using System.Text;

namespace BaseGameLab.Vfx.Editor
{
    /// <summary>What the import did and did not do, shown in the inspector and kept as a text note beside the prefab.</summary>
    internal sealed class VfxReport
    {
        public readonly string Effect;
        public readonly List<string> Built = new List<string>();
        public readonly List<string> Dropped = new List<string>();
        public readonly List<string> Attention = new List<string>();

        public VfxReport(string effect) { Effect = effect; }

        public string ToText()
        {
            var b = new StringBuilder();
            b.AppendLine(Effect);
            Section(b, "Needs attention", Attention);
            Section(b, "Did not map", Dropped);
            Section(b, "Built", Built);
            return b.ToString();
        }

        static void Section(StringBuilder b, string title, List<string> lines)
        {
            if (lines.Count == 0) return;
            b.AppendLine();
            b.AppendLine(title + ":");
            foreach (var l in lines) b.AppendLine("- " + l);
        }
    }
}
