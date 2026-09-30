using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Disappearance.GameplayQoL
{
    internal sealed class CheckpointProgress
    {
        internal const int CurrentSchema = 2;
        internal int UnlockedMask = 1;
        internal bool AllPointsUnlocked;
        internal bool HasResume;
        internal int ResumeStage;
        internal bool ResumeWithMosaic;

        internal bool IsUnlocked(int stage)
        {
            // Bit 0 is the legacy seed, not evidence of reaching the first checkpoint.
            return CheckpointStages.Valid(stage) && (UnlockedMask & (1 << stage)) != 0 &&
                (stage != 0 || HasResume || UnlockedMask != 1 || AllPointsUnlocked);
        }

        internal void Unlock(int stage)
        {
            if (!CheckpointStages.Valid(stage)) throw new ArgumentOutOfRangeException(nameof(stage));
            UnlockedMask |= 1 << stage;
        }

        internal void UnlockAll()
        {
            UnlockedMask = CheckpointStages.AllMask;
            AllPointsUnlocked = true;
        }

        internal CheckpointProgress Clone()
        {
            return (CheckpointProgress)MemberwiseClone();
        }
    }

    internal sealed class CheckpointProgressStore
    {
        private static readonly Regex IntegerField = new Regex(
            "\\\"(?<name>schemaVersion|unlockedMask|resumeStage)\\\"\\s*:\\s*(?<value>-?[0-9]+)",
            RegexOptions.CultureInvariant);
        private static readonly Regex BooleanField = new Regex(
            "\\\"(?<name>allPointsUnlocked|hasResume|resumeWithMosaic)\\\"\\s*:\\s*(?<value>true|false)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private readonly string path;
        private readonly Action<string> log;
        internal CheckpointProgress Progress { get; private set; }

        internal CheckpointProgressStore(string path, Action<string> log)
        {
            this.path = path ?? throw new ArgumentNullException(nameof(path));
            this.log = log ?? (_ => { });
            Progress = Load();
        }

        internal bool RecordArrival(int stage, bool withMosaic)
        {
            if (!CheckpointStages.Valid(stage)) throw new ArgumentOutOfRangeException(nameof(stage));
            CheckpointProgress next = Progress.Clone();
            foreach (int unlocked in CheckpointStages.Order)
                if (CheckpointStages.Rank(unlocked) <= CheckpointStages.Rank(stage)) next.Unlock(unlocked);
            next.HasResume = true;
            next.ResumeStage = stage;
            next.ResumeWithMosaic = withMosaic;
            if (Equivalent(next, Progress)) return false;
            Save(next);
            return true;
        }

        internal bool SelectResume(int stage, bool withMosaic)
        {
            if (!Progress.IsUnlocked(stage)) return false;
            CheckpointProgress next = Progress.Clone();
            next.HasResume = true;
            next.ResumeStage = stage;
            next.ResumeWithMosaic = withMosaic;
            if (!Equivalent(next, Progress)) Save(next);
            return true;
        }

        internal bool UnlockAll()
        {
            if (Progress.AllPointsUnlocked && Progress.UnlockedMask == CheckpointStages.AllMask) return false;
            CheckpointProgress next = Progress.Clone();
            next.UnlockAll();
            Save(next);
            return true;
        }

        private CheckpointProgress Load()
        {
            if (!File.Exists(path)) return new CheckpointProgress();
            try
            {
                string json = File.ReadAllText(path);
                int schema = ReadInteger(json, "schemaVersion");
                int mask = ReadInteger(json, "unlockedMask");
                int resumeStage = ReadInteger(json, "resumeStage");
                bool all = ReadBoolean(json, "allPointsUnlocked");
                bool hasResume = ReadBoolean(json, "hasResume");
                bool mosaic = ReadBoolean(json, "resumeWithMosaic");
                if ((schema != 1 && schema != CheckpointProgress.CurrentSchema) || mask < 1 ||
                    mask > (schema == 1 ? 0x0f : CheckpointStages.AllMask) || !CheckpointStages.Valid(resumeStage) ||
                    schema == 1 && resumeStage > 3 || hasResume && (mask & (1 << resumeStage)) == 0)
                    throw new InvalidDataException("unsupported or inconsistent checkpoint progress");
                // Old saves that passed the village already passed this new point.
                if (schema == 1 && (all || (mask & 0x0e) != 0)) mask |= 1 << CheckpointStages.CrewDeparted;
                if (all) mask = CheckpointStages.AllMask;
                return new CheckpointProgress {
                    UnlockedMask = mask,
                    AllPointsUnlocked = all,
                    HasResume = hasResume,
                    ResumeStage = resumeStage,
                    ResumeWithMosaic = mosaic
                };
            }
            catch (Exception exception)
            {
                log("Checkpoint progress was not loaded; the existing file was preserved: " + exception.Message);
                return new CheckpointProgress();
            }
        }

        private void Save(CheckpointProgress next)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            string backup = path + ".bak";
            string json = "{\n" +
                "  \"schemaVersion\": " + CheckpointProgress.CurrentSchema.ToString(CultureInfo.InvariantCulture) + ",\n" +
                "  \"unlockedMask\": " + next.UnlockedMask.ToString(CultureInfo.InvariantCulture) + ",\n" +
                "  \"allPointsUnlocked\": " + Bool(next.AllPointsUnlocked) + ",\n" +
                "  \"hasResume\": " + Bool(next.HasResume) + ",\n" +
                "  \"resumeStage\": " + next.ResumeStage.ToString(CultureInfo.InvariantCulture) + ",\n" +
                "  \"resumeWithMosaic\": " + Bool(next.ResumeWithMosaic) + "\n" +
                "}\n";
            File.WriteAllText(temporary, json);
            if (File.Exists(path)) File.Replace(temporary, path, backup, true);
            else File.Move(temporary, path);
            Progress = next;
        }

        private static int ReadInteger(string json, string name)
        {
            foreach (Match match in IntegerField.Matches(json))
                if (match.Groups["name"].Value == name)
                    return int.Parse(match.Groups["value"].Value, CultureInfo.InvariantCulture);
            throw new InvalidDataException("missing " + name);
        }

        private static bool ReadBoolean(string json, string name)
        {
            foreach (Match match in BooleanField.Matches(json))
                if (match.Groups["name"].Value == name)
                    return bool.Parse(match.Groups["value"].Value);
            throw new InvalidDataException("missing " + name);
        }

        private static string Bool(bool value) { return value ? "true" : "false"; }

        private static bool Equivalent(CheckpointProgress left, CheckpointProgress right)
        {
            return left.UnlockedMask == right.UnlockedMask &&
                left.AllPointsUnlocked == right.AllPointsUnlocked &&
                left.HasResume == right.HasResume &&
                left.ResumeStage == right.ResumeStage &&
                left.ResumeWithMosaic == right.ResumeWithMosaic;
        }
    }
}
