using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexusForever.GameTable;
using NexusForever.GameTable.Model;
using NexusForever.Network.Message;
using NexusForever.Network.World.Message.Model.Support;
using NexusForever.WorldServer.Configuration.Model;
using NexusForever.WorldServer.Network;

namespace NexusForever.WorldServer.Network.Message.Handler.Support
{
    public class ClientReportBugHandler : IMessageHandler<IWorldSession, ClientReportBug>
    {
        private static readonly SemaphoreSlim FileGate = new(1, 1);

        private readonly ILogger<ClientReportBugHandler> log;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly IOptionsMonitor<BugReportingConfig> bugReporting;
        private readonly IGameTableManager gameTableManager;

        public ClientReportBugHandler(
            ILogger<ClientReportBugHandler> log,
            IHttpClientFactory httpClientFactory,
            IOptionsMonitor<BugReportingConfig> bugReporting,
            IGameTableManager gameTableManager)
        {
            this.log                 = log;
            this.httpClientFactory   = httpClientFactory;
            this.bugReporting        = bugReporting;
            this.gameTableManager    = gameTableManager;
        }

        public void HandleMessage(IWorldSession session, ClientReportBug bug)
        {
            BugReportingConfig cfg = bugReporting.CurrentValue;

            string accountEmail = session.Account?.Email ?? "";
            string characterName = session.Player?.Name ?? "";
            uint? worldId = session.Player?.Map?.Entry?.Id;
            string worldAsset = session.Player?.Map?.Entry?.AssetPath ?? "";
            float px = 0f, py = 0f, pz = 0f;
            if (session.Player != null)
            {
                px = session.Player.Position.X;
                py = session.Player.Position.Y;
                pz = session.Player.Position.Z;
            }

            (uint catId, uint subId, string catLabel, string subLabel) = ResolveBugClassification(
                gameTableManager,
                bug.BugCategoryTableId,
                bug.BugSubcategoryTableId);

            var record = new BugReportRecord(
                DateTime.UtcNow,
                accountEmail,
                characterName,
                worldId,
                worldAsset,
                px, py, pz,
                catId,
                subId,
                catLabel,
                subLabel,
                bug.BugCategoryTableId,
                bug.BugSubcategoryTableId,
                bug.SelectedUnitId,
                bug.Quest2Id,
                bug.Description ?? "");

            _ = Task.Run(() => PublishAsync(cfg, record));
        }

        /// <summary>
        /// Map raw client fields to BugCategory/BugSubcategory rows and English labels.
        /// Wire order is ushort category + ushort subcategory (then uint unit / quest + wide string).
        /// </summary>
        private static (uint categoryId, uint subcategoryId, string categoryLabel, string subcategoryLabel)
            ResolveBugClassification(IGameTableManager tables, ushort rawCat, ushort rawSub)
        {
            if (tables?.BugCategory == null || tables.BugSubcategory == null)
            {
                return (
                    rawCat,
                    rawSub,
                    rawCat == 0 ? "" : $"BugCategory:{rawCat}",
                    rawSub == 0 ? "" : $"BugSubcategory:{rawSub}");
            }

            static string LocalizedOrNull(IGameTableManager m, uint localizedTextId)
            {
                if (localizedTextId == 0 || m.TextEnglish == null)
                    return null;

                return m.TextEnglish.GetEntry(localizedTextId);
            }

            BugCategoryEntry cat = tables.BugCategory.GetEntry(rawCat);
            BugSubcategoryEntry sub = tables.BugSubcategory.GetEntry(rawSub);

            if (cat != null && sub != null && sub.BugCategoryId == rawCat)
            {
                return (
                    rawCat,
                    rawSub,
                    LocalizedOrNull(tables, cat.LocalizedTextId) ?? $"BugCategory:{rawCat}",
                    LocalizedOrNull(tables, sub.LocalizedTextId) ?? $"BugSubcategory:{rawSub}");
            }

            if (rawCat == 0 && sub != null)
            {
                uint parentId = sub.BugCategoryId;
                BugCategoryEntry parent = tables.BugCategory.GetEntry(parentId);
                return (
                    parentId,
                    rawSub,
                    parent != null
                        ? LocalizedOrNull(tables, parent.LocalizedTextId) ?? $"BugCategory:{parentId}"
                        : $"BugCategory:{parentId}",
                    LocalizedOrNull(tables, sub.LocalizedTextId) ?? $"BugSubcategory:{rawSub}");
            }

            BugSubcategoryEntry subInPrimarySlot = tables.BugSubcategory.GetEntry(rawCat);
            if (subInPrimarySlot != null && rawSub == 0)
            {
                uint parentId = subInPrimarySlot.BugCategoryId;
                BugCategoryEntry parent = tables.BugCategory.GetEntry(parentId);
                return (
                    parentId,
                    rawCat,
                    parent != null
                        ? LocalizedOrNull(tables, parent.LocalizedTextId) ?? $"BugCategory:{parentId}"
                        : $"BugCategory:{parentId}",
                    LocalizedOrNull(tables, subInPrimarySlot.LocalizedTextId) ?? $"BugSubcategory:{rawCat}");
            }

            string cLabel = cat != null
                ? LocalizedOrNull(tables, cat.LocalizedTextId) ?? $"BugCategory:{rawCat}"
                : rawCat == 0 ? "" : $"BugCategory:{rawCat}";
            string sLabel = sub != null
                ? LocalizedOrNull(tables, sub.LocalizedTextId) ?? $"BugSubcategory:{rawSub}"
                : rawSub == 0 ? "" : $"BugSubcategory:{rawSub}";
            return (rawCat, rawSub, cLabel, sLabel);
        }

        private async Task PublishAsync(BugReportingConfig cfg, BugReportRecord record)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(cfg.LocalLogPath))
                    await AppendLocalLogAsync(cfg.LocalLogPath, record).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(cfg.GitHubRepository)
                    && !string.IsNullOrWhiteSpace(cfg.GitHubToken))
                    await CreateGitHubIssueAsync(cfg, record).ConfigureAwait(false);
                else if (!string.IsNullOrWhiteSpace(cfg.GitHubRepository)
                         && string.IsNullOrWhiteSpace(cfg.GitHubToken))
                    log.LogWarning("BugReporting: GitHubRepository is set but GitHubToken is empty; skipping GitHub export.");
            }
            catch (Exception e)
            {
                log.LogError(e, "BugReporting: failed to export in-game bug report.");
            }
        }

        private static async Task AppendLocalLogAsync(string path, BugReportRecord record)
        {
            await FileGate.WaitAsync().ConfigureAwait(false);
            try
            {
                string dir = global::System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                string line = JsonSerializer.Serialize(record);
                await File.AppendAllTextAsync(path, line + Environment.NewLine).ConfigureAwait(false);
            }
            finally
            {
                FileGate.Release();
            }
        }

        private async Task CreateGitHubIssueAsync(BugReportingConfig cfg, BugReportRecord record)
        {
            string[] parts = cfg.GitHubRepository.Split('/', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
            {
                log.LogWarning("BugReporting: GitHubRepository must be owner/repo (got {Repo}).", cfg.GitHubRepository);
                return;
            }

            string owner = parts[0];
            string repo = parts[1];
            string url = $"https://api.github.com/repos/{owner}/{repo}/issues";

            string title = BuildTitle(record);
            if (title.Length > 256)
                title = title[..253] + "...";

            string body = BuildBody(record);

            string[] labels = string.IsNullOrWhiteSpace(cfg.GitHubLabels)
                ? Array.Empty<string>()
                : cfg.GitHubLabels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var payload = new GitHubCreateIssueRequest
            {
                Title = title,
                Body = body,
                Labels = labels.Length > 0 ? labels : Array.Empty<string>()
            };

            HttpClient http = httpClientFactory.CreateClient("github-bug-reports");
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.UserAgent.ParseAdd("NexusForever-WorldServer/1.0 (+https://www.emulator.ws/)");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.GitHubToken.Trim());
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using HttpResponseMessage response = await http.SendAsync(request).ConfigureAwait(false);
            string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("BugReporting: GitHub API returned {Status}: {Body}", (int)response.StatusCode, responseText);
                return;
            }

            log.LogInformation("BugReporting: created GitHub issue from in-game report.");
        }

        private static string BuildTitle(BugReportRecord record)
        {
            string who = string.IsNullOrEmpty(record.CharacterName) ? record.AccountEmail : record.CharacterName;
            string topic = string.IsNullOrEmpty(record.BugSubcategoryName)
                ? record.BugCategoryName
                : $"{record.BugCategoryName} / {record.BugSubcategoryName}";
            if (string.IsNullOrWhiteSpace(topic))
                topic = "Bug report";
            return $"[In-game] {topic} — {who}";
        }

        private static string BuildBody(BugReportRecord record)
        {
            var sb = new StringBuilder();
            sb.AppendLine("### In-game bug report (NexusForever)");
            sb.AppendLine();
            sb.AppendLine($"**UTC time:** `{record.Utc:o}`");
            sb.AppendLine($"**Account:** `{EscapeMd(record.AccountEmail)}`");
            sb.AppendLine($"**Character:** `{EscapeMd(record.CharacterName)}`");
            if (record.WorldId.HasValue)
                sb.AppendLine($"**WorldId:** `{record.WorldId}`  **Asset:** `{EscapeMd(record.WorldAsset)}`");
            sb.AppendLine($"**Position:** `{record.PositionX:F2}`, `{record.PositionY:F2}`, `{record.PositionZ:F2}`");
            sb.AppendLine($"**Bug category (primary):** {EscapeMd(record.BugCategoryName)}  (`BugCategory:{record.BugCategoryId}`, client `{record.RawBugCategoryTableId}`)");
            sb.AppendLine($"**Bug subcategory (secondary):** {EscapeMd(record.BugSubcategoryName)}  (`BugSubcategory:{record.BugSubcategoryId}`, client `{record.RawBugSubcategoryTableId}`)");
            sb.AppendLine($"**SelectedUnitId:** `{record.SelectedUnitId}`  **Quest2Id:** `{record.Quest2Id}`");
            sb.AppendLine();
            sb.AppendLine("### Description");
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrWhiteSpace(record.Description) ? "_(empty)_" : record.Description);
            return sb.ToString();
        }

        private static string EscapeMd(string s)
        {
            return s.Replace("`", "\\`");
        }

        private sealed record BugReportRecord(
            DateTime Utc,
            string AccountEmail,
            string CharacterName,
            uint? WorldId,
            string WorldAsset,
            float PositionX,
            float PositionY,
            float PositionZ,
            uint BugCategoryId,
            uint BugSubcategoryId,
            string BugCategoryName,
            string BugSubcategoryName,
            ushort RawBugCategoryTableId,
            ushort RawBugSubcategoryTableId,
            uint SelectedUnitId,
            uint Quest2Id,
            string Description);

        private sealed class GitHubCreateIssueRequest
        {
            [JsonPropertyName("title")]
            public string Title { get; init; }

            [JsonPropertyName("body")]
            public string Body { get; init; }

            [JsonPropertyName("labels")]
            public string[] Labels { get; init; }
        }
    }
}
