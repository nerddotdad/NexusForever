namespace NexusForever.WorldServer.Configuration.Model
{
    /// <summary>
    /// Optional sinks for in-game bug reports (<see cref="NexusForever.Network.World.Message.Model.Support.ClientReportBug"/>).
    /// </summary>
    public class BugReportingConfig
    {
        /// <summary>
        /// GitHub repository in <c>owner/repo</c> form (e.g. <c>you/wildstar-bugs</c>). Requires <see cref="GitHubToken"/>.
        /// </summary>
        public string GitHubRepository { get; set; }

        /// <summary>
        /// Fine-grained PAT or classic PAT with <c>issues: write</c> for <see cref="GitHubRepository"/>.
        /// Prefer environment variable <c>BugReporting__GitHubToken</c> instead of storing in JSON.
        /// </summary>
        public string GitHubToken { get; set; }

        /// <summary>
        /// If set, each report is appended as one JSON line (UTF-8). Parent directories are created as needed.
        /// </summary>
        public string LocalLogPath { get; set; }

        /// <summary>
        /// Optional comma-separated GitHub label names (labels must already exist on the repo or issue creation fails).
        /// </summary>
        public string GitHubLabels { get; set; }
    }
}
