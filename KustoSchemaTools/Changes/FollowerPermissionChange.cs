using Kusto.Language;
using KustoSchemaTools.Model;
using KustoSchemaTools.Parser;
using System.Text;

namespace KustoSchemaTools.Changes
{
    /// <summary>
    /// Permission diff for follower databases. Uses follower-specific commands so
    /// we don't attempt to write directly to the read-only follower database.
    /// </summary>
    public class FollowerPermissionChange : BaseChange<List<AADObject>>
    {
        public FollowerPermissionChange(string db, string entity, List<AADObject> from, List<AADObject> to, string? leaderName, string? currentLeaderName)
            : base("FollowerPermissions", entity, from ?? new List<AADObject>(), to)
        {
            Db = db;
            LeaderName = leaderName;
            CurrentLeaderName = currentLeaderName;
            Init();
        }

        public string Db { get; }
        public string? LeaderName { get; }
        public string? CurrentLeaderName { get; }

        private string BuildCommand(List<AADObject> principals, string? leaderName)
        {
            // Order-insensitive: sort by Id so equivalent sets don't emit churn
            var ids = principals
                .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(a => "\"" + a.Id + "\""
                );

            var hasPrincipals = ids.Any();

            // Kusto control commands expect the literal keyword 'none' when no principals
            // are supplied. Wrapping none in parentheses makes it a principal named "none"
            // and the command is rejected, so only use parentheses for non-empty sets.
            var idsFragment = hasPrincipals ? $"({string.Join(",", ids)})" : "none";

            // Leader name is only relevant when principals are supplied; Kusto rejects
            // bare leader suffixes following 'none', and they add noise to diffs. Emit
            // the leader only when we have principals to set.
            var leaderSuffix = hasPrincipals && !string.IsNullOrWhiteSpace(leaderName)
                ? $" '{leaderName}'"
                : string.Empty;

            return $".set follower database {Db.BracketIfIdentifier()} {Entity.ToLower()} {idsFragment}{leaderSuffix}";
        }

        private void Init()
        {
            var targetCmd = BuildCommand(To, LeaderName);
            var currentCmd = BuildCommand(From, CurrentLeaderName);

            if (!string.Equals(targetCmd, currentCmd, StringComparison.Ordinal))
            {
                var script = new DatabaseScript { Text = targetCmd, Order = 0 };
                var container = new DatabaseScriptContainer(script, "FollowerPermissionChange");
                var code = KustoCode.Parse(script.Text);
                container.IsValid = !code.GetDiagnostics().Any();
                Scripts.Add(container);
            }

            var added = To.Where(itm => From.All(t => t.Id != itm.Id)).ToList();
            var removed = From.Where(itm => To.All(t => t.Id != itm.Id)).ToList();
            var changed = From.Join(To, f => f.Id, t => t.Id, (f, t) => new { f, t })
                              .Where(x => x.f.Name != x.t.Name)
                              .ToList();

            var sb = new StringBuilder();
            sb.AppendLine($"## {Entity} (Follower)");
            sb.AppendLine();
            sb.AppendLine("<table>");
            sb.AppendLine("<tr></tr>");

            if (added.Any())
            {
                sb.AppendLine("<tr><td colspan=\"2\">Added:</td><td colspan=\"10\">" + string.Join("<br>", added.Select(t => $"{t.Name} ({t.Id})")) + "</td></tr>");
            }
            if (removed.Any())
            {
                sb.AppendLine("<tr><td colspan=\"2\">Removed:</td><td colspan=\"10\">" + string.Join("<br>", removed.Select(t => $"{t.Name} ({t.Id})")) + "</td></tr>");
            }
            if (changed.Any())
            {
                Scripts.Add(new DatabaseScriptContainer("FollowerPermissionRenamed", -1, "// No Database Change"));
                sb.AppendLine("<tr><td colspan=\"2\">Changed:</td><td colspan=\"10\">" + string.Join("<br>", changed.Select(t => $"{t.f.Name} => {t.t.Name} ({t.t.Id})")) + "</td></tr>");
            }

            var logo = Scripts.Any() && Scripts.First().IsValid == false ? ":red_circle:" : ":green_circle:";
            var displayCmd = Scripts.FirstOrDefault()?.Script?.Text ?? currentCmd;
            sb.AppendLine($"<tr><td colspan=\"2\">{logo}</td><td colspan=\"10\"><pre lang=\"kql\">{displayCmd.PrettifyKql()}</pre></td></tr>");
            sb.AppendLine("</table>");

            Markdown = sb.ToString();
        }
    }
}
