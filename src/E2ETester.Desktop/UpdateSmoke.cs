using System.IO;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace E2ETester.Desktop;
internal static class UpdateSmoke
{
    public static async Task RunAsync(string[] args, string screenshot)
    {
        var sourcePath = Path.GetFullPath(args.Single(a => a.StartsWith("--update-exercise="))[18..]);
        var targetVersion = args.Single(a => a.StartsWith("--update-target="))[16..];
        var source = new SimpleFileSource(new DirectoryInfo(sourcePath));
        var manager = new UpdateManager(source);
        if (!manager.IsInstalled || !manager.IsPortable) throw new InvalidOperationException("Update smoke requires an isolated Velopack portable bundle, not a real installation.");
        var feed = await source.GetReleaseFeed(NullVelopackLogger.Instance, manager.AppId, "win");
        var target = feed.Assets.Single(a => a.Type == VelopackAssetType.Full && a.Version.ToString() == targetVersion);
        var downgrade = target.Version < manager.CurrentVersion;
        var update = new UpdateInfo(target, downgrade);
        await manager.DownloadUpdatesAsync(update);
        await File.WriteAllTextAsync(screenshot + ".prepared.txt", $"{manager.CurrentVersion} -> {target.Version}; downgrade={downgrade}");
        manager.ApplyUpdatesAndRestart(target, ["--smoke=" + screenshot + ".applied.png", "--assert-version=" + targetVersion]);
    }
}
