using Kvertis.Engine.Abstractions;
using Kvertis.Engine.Estimation;

namespace Kvertis.Queue.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    private static HistoryEntry Entry(int i, bool success = true) => new(
        Guid.NewGuid(),
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(i),
        $"/in/file{i}.jpg",
        new FormatId("jpg"),
        success ? $"/out/file{i}.png" : null,
        new ConversionSettings(new FormatId("png"), Quality: 70, TargetSizeBytes: 25_000_000, Metadata: MetadataPolicy.Keep,
            Preset: ConversionPreset.Email, Advanced: new Dictionary<string, string> { ["maxDimension"] = "1080" }),
        success,
        success ? ConversionErrorCode.None : ConversionErrorCode.CorruptFile,
        1000 + i,
        500 + i,
        TimeSpan.FromMilliseconds(1234));

    [Fact]
    public async Task History_store_round_trips_entries()
    {
        var store = new JsonFileHistoryStore(_temp.Combine("sub", "history.json"));
        var entries = new[] { Entry(2), Entry(1, success: false) };

        await store.SaveAsync(entries, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        loaded.Count.ShouldBe(2);
        loaded[0].ShouldBe(entries[0] with { Settings = loaded[0].Settings });
        loaded[0].Settings.Output.ShouldBe(new FormatId("png"));
        loaded[0].Settings.Quality.ShouldBe(70);
        loaded[0].Settings.TargetSizeBytes.ShouldBe(25_000_000);
        loaded[0].Settings.Metadata.ShouldBe(MetadataPolicy.Keep);
        loaded[0].Settings.Preset.ShouldBe(ConversionPreset.Email);
        loaded[0].Settings.GetAdvanced("maxDimension").ShouldBe("1080");
        loaded[1].Error.ShouldBe(ConversionErrorCode.CorruptFile);
        loaded[1].OutputPath.ShouldBeNull();
        (await File.ReadAllTextAsync(_temp.Combine("sub", "history.json"))).ShouldContain("\"corruptFile\"", Case.Insensitive);
    }

    [Fact]
    public async Task History_store_keeps_newest_200()
    {
        var store = new JsonFileHistoryStore(_temp.Combine("history.json"));
        var entries = Enumerable.Range(0, 250).Select(i => Entry(i)).ToList();

        await store.SaveAsync(entries, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        loaded.Count.ShouldBe(JsonFileHistoryStore.MaxEntries);
        loaded[0].InputPath.ShouldBe("/in/file249.jpg");
        loaded[^1].InputPath.ShouldBe("/in/file50.jpg");
    }

    [Fact]
    public async Task History_store_treats_missing_or_corrupt_file_as_empty()
    {
        var path = _temp.Combine("history.json");
        var store = new JsonFileHistoryStore(path);
        (await store.LoadAsync(CancellationToken.None)).ShouldBeEmpty();

        await File.WriteAllTextAsync(path, "{ not json");
        (await store.LoadAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task JobHistory_caps_entries_newest_first_and_persists()
    {
        var path = _temp.Combine("history.json");
        using var history = new JobHistory(new JsonFileHistoryStore(path), maxEntries: 3);
        for (var i = 0; i < 5; i++)
        {
            await history.AddAsync(Entry(i));
        }

        history.Entries.Select(e => e.InputPath).ShouldBe(["/in/file4.jpg", "/in/file3.jpg", "/in/file2.jpg"]);

        using var reloaded = new JobHistory(new JsonFileHistoryStore(path));
        await reloaded.LoadAsync();
        reloaded.Entries.Count.ShouldBe(3);
        reloaded.Entries[0].InputPath.ShouldBe("/in/file4.jpg");
    }

    [Fact]
    public async Task Speed_profile_store_saves_and_loads()
    {
        var path = _temp.Combine("speed.json");
        await using (var store = new JsonFileSpeedProfileStore(path, debounce: TimeSpan.FromHours(1)))
        {
            var profile = await store.LoadAsync();
            profile.Record("Image:jpg>png", 12.5, 0.4);
            store.RequestSave();
            store.RequestSave();
            File.Exists(path).ShouldBeFalse(); // debounced
            await store.FlushAsync();
            File.Exists(path).ShouldBeTrue();
        }

        await using var again = new JsonFileSpeedProfileStore(path);
        var loaded = await again.LoadAsync();
        loaded.Get("Image:jpg>png").ShouldNotBeNull().UnitsPerSecond.ShouldBe(12.5);
    }

    [Fact]
    public async Task Speed_profile_store_writes_after_debounce()
    {
        var path = _temp.Combine("speed.json");
        await using var store = new JsonFileSpeedProfileStore(path, debounce: TimeSpan.Zero);
        var profile = await store.LoadAsync();
        profile.Record("Audio:wav>mp3", 60, 0.1);

        store.RequestSave();
        await store.PendingSave.WaitAsync(QueueHarness.Timeout);

        SpeedProfile.FromJson(await File.ReadAllTextAsync(path)).Get("Audio:wav>mp3").ShouldNotBeNull();
    }

    [Fact]
    public async Task Corrupt_speed_profile_starts_empty()
    {
        var path = _temp.Combine("speed.json");
        await File.WriteAllTextAsync(path, "garbage");
        await using var store = new JsonFileSpeedProfileStore(path);
        (await store.LoadAsync()).Get("x").ShouldBeNull();
    }
}
