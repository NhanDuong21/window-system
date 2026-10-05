using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Nyan.Core;

/// <summary>Integration checks write only a newly-created marked fixture below the supplied test root.</summary>
public static class StoreChecks
{
    public static async Task<List<string>> RunAsync(string fixtureRoot)
    {
        var parent = Path.GetFullPath(fixtureRoot);
        if (!Directory.Exists(parent) || !File.Exists(Path.Combine(parent, ".nyan-fixture")))
            throw new AppException("fixture_required", "Kiểm thử chỉ chạy dưới thư mục có marker .nyan-fixture.");
        var root = Path.Combine(parent, "storage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".nyan-fixture"), "Nyan storage integration fixture");
        var results = new List<string>();
        var data = CreateOwned("store");
        var store = new AppStore(data);
        store.SaveSettings(new(Dark: true));
        Check(new AppStore(data).Settings.Dark, "DPAPI state reload / settings");
        var before = SnapshotOf("before", "1", "First");
        var after = SnapshotOf("after", "2", "Second");
        store.SaveSnapshot(before);
        store.SaveSnapshot(after);
        var changes = store.CompareSnapshots("before", "after");
        Check(changes.Count == 1 && changes[0].Kind == "Changed" && changes[0].Before == "First" && changes[0].After == "Second", "Snapshot stable-ID field diff");
        store.SaveSnapshot(new("added", "Added", DateTimeOffset.Now, 1, new() { ["Applications"] = [new("other", new() { ["name"] = "Other" }), Coverage()] }));
        var replaced = store.CompareSnapshots("before", "added");
        Check(replaced.Count == 2 && replaced.Any(change => change.Kind == "Added") && replaced.Any(change => change.Kind == "Removed"), "Snapshot added / removed diff");
        store.SaveSnapshot(new("partial", "Partial", DateTimeOffset.Now, 1, new() { ["Applications"] = [Coverage("Partial")] }));
        var partialDiff = store.CompareSnapshots("before", "partial");
        Check(partialDiff.Count == 1 && partialDiff[0].Kind == "Coverage", "Incomplete snapshot suppresses false removal");
        var exported = Path.Combine(root, "test.nccsnapshot");
        store.ExportSnapshot("before", exported);
        var imported = new AppStore(CreateOwned("import"));
        imported.ImportSnapshot(exported);
        Check(imported.GetSnapshot("before").Name == before.Name, "Same-user DPAPI snapshot import / export");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(exported)).Contains("First", StringComparison.Ordinal), "Snapshot export contains no plaintext fixture values");
        store.PutBackup("undo-fixture", new() { ["kind"] = "Environment", ["value"] = "exact fixture token value; ü", ["exists"] = "true" });
        Check(new AppStore(data).GetBackup("undo-fixture")["value"] == "exact fixture token value; ü", "DPAPI undo preserves exact original");
        Check(store.GetBackups().Single()["id"] == "undo-fixture", "Undo inventory supplies reserved ID");
        var backup = Path.Combine(root, "all.nccbackup");
        store.Backup(backup);
        store.SaveSettings(new(Dark: false));
        store.RemoveBackup("undo-fixture");
        store.Restore(backup);
        Check(store.Settings.Dark && store.GetBackup("undo-fixture")["exists"] == "true", "Transactional backup restore includes settings and undo state");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(backup)).Contains("exact fixture token", StringComparison.Ordinal), "Backup contains no plaintext undo");
        store.AddHistory(new("old", DateTimeOffset.UtcNow.AddDays(-31), "fixture", "fixture", "OK", "expired"));
        store.AddHistory(new("new", DateTimeOffset.UtcNow, "fixture", "fixture", "OK", "current"));
        Check(store.GetHistory().Count == 1 && store.GetHistory()[0].Id == "new", "History retention 30 days");
        Expect(() => store.SaveSnapshot(new("unsafe", "Unsafe", DateTimeOffset.Now, 1, new() { ["Environment"] = [new("row", new() { ["value"] = "must not persist" })] })), "Snapshot environment value rejected");
        Expect(() => store.SaveSnapshot(new("unsafe", "Unsafe", DateTimeOffset.Now, 1, new() { ["Startup"] = [new("row", new() { ["name"] = "Fixture" }, new() { ["command"] = "unsafe metadata" })] })), "Snapshot control metadata rejected");
        var invalid = Path.Combine(root, "invalid.nccsnapshot");
        File.WriteAllText(invalid, "{\"SchemaVersion\":1}");
        Expect(() => imported.ImportSnapshot(invalid), "Plaintext / malformed import rejected");
        Expect(() => store.Restore(exported), "Wrong backup extension rejected");
        var legacy = CreateOwned("legacy");
        File.WriteAllText(Path.Combine(legacy, "state.v0.json"), JsonSerializer.Serialize(new
        {
            SchemaVersion = 0, Settings = new AppSettings(0, true, 30),
            History = Array.Empty<HistoryEntry>(), Snapshots = Array.Empty<Snapshot>(), Backups = new Dictionary<string, Dictionary<string, string>>()
        }));
        var migrated = new AppStore(legacy);
        Check(migrated.Settings is { SchemaVersion: 1, Dark: true } && File.Exists(Path.Combine(legacy, "state.ncc")) && !File.Exists(Path.Combine(legacy, "state.v0.json")), "Owned fixture schema-0 migration to encrypted schema-1");
        var corrupt = CreateOwned("corrupt");
        var damaged = new AppStore(corrupt);
        damaged.SaveSettings(new());
        var corruptFile = Path.Combine(corrupt, "state.ncc");
        var originalBytes = File.ReadAllBytes(corruptFile);
        originalBytes[^1] ^= 0xFF;
        File.WriteAllBytes(corruptFile, originalBytes);
        Expect(() => new AppStore(corrupt), "Corrupt DPAPI state reported");
        Check(File.ReadAllBytes(corruptFile).SequenceEqual(originalBytes) && Directory.EnumerateFiles(corrupt, "corrupt-*.ncc").Any(), "Corruption preserves original and evidence copy");
        Check(store.GetSnapshots().Count == 4, "Rejected writes preserve existing state");

        var quotaRoot = CreateOwned("quota");
        var quotaBackups = Enumerable.Range(0, 100).ToDictionary(index => "quota-undo-" + index, index => new Dictionary<string, string>
        {
            ["type"] = "environment", ["scope"] = "User", ["name"] = "NYAN_QUOTA_FIXTURE_" + index,
            ["value"] = "fixture secret retained only in DPAPI", ["exists"] = "True", ["kind"] = "String", ["afterFingerprint"] = "fixture-after"
        });
        var quotaHistory = Enumerable.Range(0, 100).Select(index => new HistoryEntry("quota-history-" + index, DateTimeOffset.UtcNow.AddDays(-31), "fixture", "fixture", "success", "fixture", "quota-undo-" + index)).ToList();
        var quotaSnapshots = Enumerable.Range(0, 100).Select(index => new Snapshot("quota-snapshot-" + index, "Fixture " + index, DateTimeOffset.Now, 1, new() { ["Applications"] = [Coverage("Empty")] })).ToList();
        SeedEncryptedState(quotaRoot, quotaHistory, quotaSnapshots, quotaBackups);
        var quota = new AppStore(quotaRoot);
        var inventory = quota.GetStoredDataInventory();
        Check(quota.GetHistory().Count == 0 && inventory.BackupLimit == 100 && inventory.SnapshotLimit == 100 && inventory.Items.Count == 200 && inventory.Items.Count(item => item.Kind == "environment" && item.CanDiscard && item.CanUndo && item.At is null) == 100, "Aged history retains discoverable undo inventory with legacy optional dates");
        Check(!JsonSerializer.Serialize(inventory).Contains("fixture secret", StringComparison.Ordinal), "Stored data inventory never exposes original values or file contents");
        Check(new AppStore(quotaRoot).GetStoredDataInventory().Items.Count == 200, "Stored data inventory survives DPAPI reload at both quotas");
        ExpectCode(() => quota.PutBackup("quota-new", new() { ["type"] = "environment" }), "limit", "Backup quota still rejects the 101st item");
        var staleUndo = quota.GetDiscardFingerprint("environment", "quota-undo-0");
        var changedUndo = quota.GetBackup("quota-undo-0");
        changedUndo["value"] = "changed fixture original";
        quota.PutBackup("quota-undo-0", changedUndo);
        ExpectCode(() => quota.DiscardData("environment", "quota-undo-0", staleUndo), "stale", "Discard rechecks changed backup payload before deleting");
        Check(quota.GetStoredDataInventory().Items.Single(item => item.Id == "quota-undo-0").At is not null, "New backup writes add a creation time without rejecting legacy data");
        quota.DiscardData("environment", "quota-undo-0", quota.GetDiscardFingerprint("environment", "quota-undo-0"));
        quota.PutBackup("quota-new", new() { ["type"] = "environment" });
        Check(new AppStore(quotaRoot).GetBackups().Count == 100 && quota.GetBackups().All(item => item["id"] != "quota-undo-0"), "Confirmed environment discard frees quota without Windows mutation");
        var staleSnapshot = quota.GetDiscardFingerprint("snapshot", "quota-snapshot-0");
        quota.SaveSnapshot(quota.GetSnapshot("quota-snapshot-0") with { Name = "Changed fixture" });
        ExpectCode(() => quota.DiscardData("snapshot", "quota-snapshot-0", staleSnapshot), "stale", "Snapshot discard rechecks changed payload");
        ExpectCode(() => quota.SaveSnapshot(new("quota-snapshot-new", "Fixture", DateTimeOffset.Now, 1, new())), "limit", "Snapshot quota still rejects the 101st item");
        quota.DiscardData("snapshot", "quota-snapshot-0", quota.GetDiscardFingerprint("snapshot", "quota-snapshot-0"));
        quota.SaveSnapshot(new("quota-snapshot-new", "Fixture", DateTimeOffset.Now, 1, new()));
        Check(new AppStore(quotaRoot).GetSnapshots().Count == 100 && quota.GetSnapshots().All(item => item.Id != "quota-snapshot-0"), "Confirmed snapshot discard frees quota and persists after reload");

        var protectedRoot = CreateOwned("protected-backups");
        var protectedFile = Path.Combine(protectedRoot, "retained-fixture.txt");
        File.WriteAllText(protectedFile, "owned fixture contents must remain");
        var protectedStore = new AppStore(protectedRoot);
        protectedStore.PutBackup("protected-startup", new() { ["type"] = "startup-file", ["name"] = "retained-fixture.txt", ["path"] = protectedFile, ["contents"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("owned fixture contents must remain")) });
        protectedStore.PutBackup("protected-quarantine", new() { ["type"] = "quarantine", ["path"] = protectedFile, ["quarantine"] = protectedFile, ["identity"] = "fixture" });
        ExpectCode(() => protectedStore.GetDiscardFingerprint("startup-file", "protected-startup"), "scope", "Startup originals cannot receive a discard preview");
        ExpectCode(() => protectedStore.DiscardData("environment", "protected-startup", "forged"), "scope", "Environment discard cannot target Startup originals");
        ExpectCode(() => protectedStore.DiscardData("quarantine", "protected-quarantine", "forged"), "scope", "Quarantine records cannot be discarded");
        Check(protectedStore.GetStoredDataInventory().Items.All(item => !item.CanDiscard) && protectedStore.GetBackups().Count == 2 && File.ReadAllText(protectedFile) == "owned fixture contents must remain", "Forbidden discard preserves both recovery records and owned files");

        var aliasStore = new AppStore(CreateOwned("snapshot-aliases"));
        foreach (var module in new[] { "Applications", "Startup" })
        {
            var canonical = Privacy.Hash(module + "-HKCU-Registry64-fixture");
            var alias = Privacy.Hash(module + "-HKCU-Registry32-fixture");
            var distinct = Privacy.Hash(module + "-HKLM-Registry32-distinct");
            var oldSource32 = module == "Applications" ? "User / Registry 32 bit" : "User / Run / 32 bit";
            var oldSource64 = module == "Applications" ? "User / Registry 64 bit" : "User / Run / 64 bit";
            var newSource = module == "Applications" ? "User / Registry dùng chung" : "User / Run / Registry dùng chung";
            var oldRows = new List<Row> { new(alias, new() { ["name"] = "Same fixture name", ["version"] = "obsolete mirror", ["source"] = oldSource32 }), new(canonical, new() { ["name"] = "Same fixture name", ["version"] = "1", ["source"] = oldSource64 }), new(distinct, new() { ["name"] = "Same fixture name", ["version"] = "1", ["source"] = "System / Registry 32 bit" }) };
            var newRows = new List<Row> { new(canonical, new() { ["name"] = "Same fixture name", ["version"] = "1", ["source"] = newSource, ["__legacyId"] = alias }), oldRows[2] };
            aliasStore.SaveSnapshot(MigrationSnapshot("old-" + module, module, oldRows));
            aliasStore.SaveSnapshot(MigrationSnapshot("new-" + module, module, newRows));
            Check(aliasStore.CompareSnapshots("old-" + module, "new-" + module).Count == 0 && aliasStore.CompareSnapshots("new-" + module, "old-" + module).Count == 0, module + " legacy aliases fold duplicate views and source labels in both directions");
            var changedRows = newRows.Select(row => row.Id == canonical ? row with { Cells = new(row.Cells) { ["version"] = "2" } } : row).ToList();
            aliasStore.SaveSnapshot(MigrationSnapshot("changed-" + module, module, changedRows));
            var aliasDiff = aliasStore.CompareSnapshots("old-" + module, "changed-" + module);
            Check(aliasDiff.Count == 1 && aliasDiff[0].Kind == "Changed" && aliasDiff[0].Key == canonical + "/version" && aliasDiff[0].Before == "1" && aliasDiff[0].After == "2", module + " alias migration preserves true field changes and canonical preference");
            aliasStore.SaveSnapshot(MigrationSnapshot("removed-" + module, module, newRows.Take(1).ToList()));
            var distinctDiff = aliasStore.CompareSnapshots("old-" + module, "removed-" + module);
            Check(distinctDiff.Count == 1 && distinctDiff[0].Kind == "Removed" && distinctDiff[0].Key == distinct, module + " aliases never merge distinct IDs with the same name");
            aliasStore.SaveSnapshot(MigrationSnapshot("empty-" + module, module, new()));
            Check(aliasStore.CompareSnapshots("empty-" + module, "new-" + module).All(change => !change.After.Contains("__legacyId", StringComparison.Ordinal)), module + " alias metadata is excluded from added row descriptions");
        }

        var scanRoot = CreateOwned("scan");
        // Marker itself is outside the scanned directory so size/count expectations are exact.
        var tree = Path.Combine(scanRoot, "cây dữ liệu");
        Directory.CreateDirectory(tree);
        var unicode = Path.Combine(tree, "tệp 日本語 ü.txt");
        File.WriteAllBytes(unicode, new byte[12345]);
        var child = Path.Combine(tree, "nhánh");
        Directory.CreateDirectory(child);
        File.WriteAllBytes(Path.Combine(child, "nested.bin"), new byte[678]);
        var hardlink = Path.Combine(tree, "hardlink.bin");
        var linked = CreateHardLink(hardlink, unicode, IntPtr.Zero);
        var scanner = new StorageScanner();
        var progress = new InlineProgress();
        var scan = await scanner.ScanAsync(tree, progress, CancellationToken.None);
        Check(scan.Files == (linked ? 3 : 2) && scan.Bytes == 13023, "Unicode scanner sizes / hard-link deduplication");
        Check(scan.AllocatedBytes is > 0 && scan.Rows.Any(row => row.Meta("kind") == "directory" && row.Meta("path") == child && row.Meta("length") == "678"), "Allocated metadata and directory aggregate rows");
        Check(scan.Rows.Any(row => row.Meta("path") == unicode && row.Meta("identity").Length > 0) && progress.Count > 0, "Scanner native identity and progress");
        Check(scan.Rows.All(row => row.Meta("allocatedBytes") == "" || long.TryParse(row.Meta("allocatedBytes"), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) && value >= 0) && (!linked || scan.Rows.Single(row => row.Meta("duplicate") == "true").Meta("allocatedBytes") == "0"), "Scanner provides invariant allocated byte metadata including deduplicated hard links");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            var cancelled = await scanner.ScanAsync(tree, null, cancellation.Token);
            Check(cancelled.Cancelled && cancelled.Files == 0, "Cancellation returns partial report");
        }
        var target = Path.Combine(scanRoot, "target");
        Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "outside.bin"), new byte[5000]);
        var symbolic = Path.Combine(tree, "linked-directory");
        if (CreateSymbolicLink(symbolic, target, 1 | 2))
        {
            var withoutLinks = await scanner.ScanAsync(tree, null, CancellationToken.None);
            Check(withoutLinks.Bytes == 13023 && withoutLinks.Skipped > 0, "Reparse directories are skipped");
            try { await scanner.ScanAsync(symbolic, null, CancellationToken.None); throw new InvalidOperationException("Root reparse accepted."); }
            catch (AppException exception) when (exception.Code == "scan_link") { results.Add("PASS Root reparse rejected"); }
        }
        else results.Add("SKIP Reparse creation unavailable (developer-mode privilege); production rejection remains implemented.");
        var offline = Path.Combine(tree, "cloud-only-fixture.bin");
        File.WriteAllBytes(offline, new byte[5000]);
        var originalAttributes = File.GetAttributes(offline);
        try
        {
            File.SetAttributes(offline, originalAttributes | FileAttributes.Offline);
            var cloudSkipped = await scanner.ScanAsync(tree, null, CancellationToken.None);
            Check(cloudSkipped.Bytes == 13023 && cloudSkipped.Skipped > 0, "Offline cloud-only attribute skipped without reading contents");
        }
        finally { File.SetAttributes(offline, originalAttributes); File.Delete(offline); }
        var deniedPath = Path.Combine(tree, "access-denied-fixture");
        Directory.CreateDirectory(deniedPath);
        File.WriteAllBytes(Path.Combine(deniedPath, "hidden.bin"), new byte[77]);
        var deniedInfo = new DirectoryInfo(deniedPath);
        var originalAcl = deniedInfo.GetAccessControl();
        var deniedAcl = deniedInfo.GetAccessControl();
        var sid = WindowsIdentity.GetCurrent().User!;
        deniedAcl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.ListDirectory, AccessControlType.Deny));
        try
        {
            deniedInfo.SetAccessControl(deniedAcl);
            var deniedScan = await scanner.ScanAsync(tree, null, CancellationToken.None);
            Check(deniedScan.Bytes == 13023 && deniedScan.Skipped > 0, "Access-denied subtree returns partial report");
        }
        finally { deniedInfo.SetAccessControl(originalAcl); }
        var longRoot = CreateOwned("long");
        var longFolder = longRoot;
        for (var index = 0; index < 7; index++) { longFolder = Path.Combine(longFolder, new string('a', 40) + index); Directory.CreateDirectory(longFolder); }
        File.WriteAllBytes(Path.Combine(longFolder, "dài 日本語.bin"), new byte[321]);
        var longScan = await scanner.ScanAsync(longRoot, null, CancellationToken.None);
        Check(longScan.Bytes == 321 + new FileInfo(Path.Combine(longRoot, ".nyan-fixture")).Length && longScan.Skipped == 0, "Long Unicode path metadata scan");
        var large = Path.Combine(scanRoot, "large");
        Directory.CreateDirectory(large);
        for (var index = 0; index < 2105; index++) File.WriteAllBytes(Path.Combine(large, index.ToString("D4") + ".bin"), new byte[1]);
        var bounded = await scanner.ScanAsync(large, null, CancellationToken.None);
        Check(bounded.Files == 2105 && bounded.Bytes == 2105 && bounded.Rows.Count == 2000, "Bounded 2000 rows retain exact counts");
        using (var activeCancellation = new CancellationTokenSource())
        {
            var activeProgress = new CancellingProgress(activeCancellation);
            var partialScan = await scanner.ScanAsync(large, activeProgress, activeCancellation.Token);
            if (activeProgress.CancelledDuringScan) Check(partialScan.Cancelled && partialScan.Files is > 0 and < 2105, "Active scan cancellation returns accumulated partial metadata");
            else results.Add("SKIP Active cancellation fixture completed before first throttled progress tick; pre-cancel verified.");
        }
        try { await scanner.ScanAsync(@"\\localhost\fixture", null, CancellationToken.None); throw new InvalidOperationException("UNC scan accepted."); }
        catch (AppException exception) when (exception.Code == "scan_path") { results.Add("PASS UNC rejected"); }
        results.Add("Fixture preserved under " + root);
        return results;

        string CreateOwned(string name)
        {
            var path = Path.Combine(root, name);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, ".nyan-fixture"), "Owned fixture");
            return path;
        }
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAIL " + name);
            results.Add("PASS " + name);
        }
        void Expect(Action action, string name)
        {
            try { action(); }
            catch (AppException) { results.Add("PASS " + name); return; }
            throw new InvalidOperationException("FAIL " + name);
        }
        void ExpectCode(Action action, string code, string name)
        {
            try { action(); }
            catch (AppException exception) when (exception.Code == code) { results.Add("PASS " + name); return; }
            throw new InvalidOperationException("FAIL " + name);
        }
    }
    private static Snapshot MigrationSnapshot(string id, string module, List<Row> rows) => new(id, "Fixture", DateTimeOffset.Now, 1, new() { [module] = rows.Concat([Coverage()]).ToList() });
    private static void SeedEncryptedState(string root, List<HistoryEntry> history, List<Snapshot> snapshots, Dictionary<string, Dictionary<string, string>> backups)
    {
        if (!File.Exists(Path.Combine(root, ".nyan-fixture"))) throw new AppException("fixture_required", "Chỉ seed state dưới fixture có marker.");
        var encrypted = NativeSecurity.Protect(JsonSerializer.SerializeToUtf8Bytes(new { SchemaVersion = 1, Settings = new AppSettings(), History = history, Snapshots = snapshots, Backups = backups }));
        var prefix = Encoding.ASCII.GetBytes("NYAN-NCC1\0");
        var payload = new byte[prefix.Length + encrypted.Length];
        prefix.CopyTo(payload, 0); encrypted.CopyTo(payload, prefix.Length);
        File.WriteAllBytes(Path.Combine(root, "state.ncc"), payload);
    }
    private static Snapshot SnapshotOf(string id, string version, string value) => new(id, "Fixture " + version, DateTimeOffset.Now, 1,
        new() { ["Applications"] = [new("stable", new() { ["name"] = "Fixture", ["version"] = value }), Coverage()] });
    private static Row Coverage(string state = "Ready") => new("__coverage", new() { ["state"] = state, ["detail"] = "" });
    private sealed class InlineProgress : IProgress<ScanProgress> { public int Count; public void Report(ScanProgress value) => Count++; }
    private sealed class CancellingProgress(CancellationTokenSource source) : IProgress<ScanProgress>
    {
        public bool CancelledDuringScan;
        public void Report(ScanProgress value) { if (value.Status.StartsWith("Đang", StringComparison.Ordinal) && value.Files > 0) { CancelledDuringScan = true; source.Cancel(); } }
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string newName, string existingName, IntPtr security);
    [DllImport("kernel32.dll", EntryPoint = "CreateSymbolicLinkW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool CreateSymbolicLink(string link, string target, uint flags);
}
