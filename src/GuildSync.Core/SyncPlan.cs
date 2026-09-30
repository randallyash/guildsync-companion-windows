namespace GuildSync.Core;

public sealed record PlannedFile(string Path, SavedVarsState State);

public sealed record SyncPlanResult(IReadOnlyList<PlannedFile> Files, string Message)
{
    public bool ShouldUpload => Files.Count > 0;
}

public static class SyncPlan
{
    public static SyncPlanResult Build(
        IReadOnlyList<string> files,
        Func<string, SyncStamp?> lastOf,
        bool force)
    {
        if (files.Count == 0)
        {
            return new SyncPlanResult([],
                "No GuildSync data found yet. Play once, then log out.");
        }

        var plan = new List<PlannedFile>();
        var invalid = 0;
        foreach (var path in files)
        {
            var state = SavedVariables.Evaluate(path, lastOf(path));
            if (state.Exists && state.Valid && (state.Changed || force))
                plan.Add(new PlannedFile(path, state));
            else if (state.Exists && !state.Valid)
                invalid++;
        }

        if (plan.Count == 0)
        {
            var message = invalid == files.Count
                ? "Found GuildSync.lua but it is not a GuildSync export (or it is empty). Play a session first."
                : "Already up to date.";
            return new SyncPlanResult([], message);
        }

        return new SyncPlanResult(plan, $"Uploading {plan.Count} file(s)...");
    }
}
