using DiscordBot.Domain.UnityNews;

namespace DiscordBot.Tests.Domain.UnityNews;

/// <summary>Small excerpts shaped like the markdown Unity publishes (the real files are 10 KB to 500 KB).</summary>
internal static class UnityNewsFixtures
{
    public const string Beta8 = """
        ### Known Issues in 6000.7.0b8

        - Android:  Back gesture/button closes the app after returning from background
            ([UUM-159613](https://issuetracker.unity.com/api/v1.0/redirects/guid/UUM-159613))

        - DX12: Fixed an issue where the Editor leaked descriptor memory.
            (UUM-159625)    <br>*Fixed in 6000.7.0b9.*

        - : Crash on mono_log_write_logfile when more than one copy of bee_backend is running
            ([UUM-100001](https://issuetracker.unity.com/api/v1.0/redirects/guid/UUM-100001))

        ### New 6000.7.0b8 Entries since 6000.7.0b7

        #### Features

        - Build Pipeline: Added a Referenced By tree to the Build Analysis asset inspector.



        #### Improvements

        - Asset Pipeline: Improved Memory Profiler attribution of Asset Database native memory.
            (UUM-126907)

        - Build Pipeline: Fixed an issue where import workers would add overhead.
            (UUM-158754)<br>
            *First seen in 6000.7.0b8.*

        - Editor: Project Auditor: The error shown when the rules package is missing now names the package.
            (UUM-156488)<br>
            *First seen in 6000.7.0b6.*

        - Entities: Calling BlobArray&lt;T&gt;.ToArray is now reported as compile error \(UAC0044\).
            ([UUM-1](https://x/UUM-1), [UUM-2](https://x/UUM-2))

        #### API Changes

        - 2D: Changed: Moved EntityIdTileUtility to the UnityEditor.Tilemaps namespace.

        - Build Pipeline: Added: Added BuildHistory.TryGetParentBuild to find the parent build.
            (UUM-153441)

        - Burst: Added: Added a new attribute.

        #### Changes

        - Editor: Raised the minimum version of the [Hub](https://unity.com/hub) to 3.0.

        #### Fixes

        - 2D: Fixed a warning when you undo a Shadow Caster 2D edit.
            ([UUM-152935](https://issuetracker.unity.com/api/v1.0/redirects/guid/UUM-152935))

        - 2D: Fixed a NullReferenceException for a Light 2D.
            ([UUM-154810](https://issuetracker.unity.com/api/v1.0/redirects/guid/UUM-154810))

        - Android: Backported crash fixes for frame pacing.
            (UUM-153366)

        - : Fixed a crash with an empty area.

        #### New 6000.7.0b8 Package Changes since 6000.7.0b7

        #### Packages updated

        - com.unity.addressables: [4.1.1](https://docs.unity3d.com/Packages/com.unity.addressables@4.1//changelog/CHANGELOG.html) to [4.1.2](https://docs.unity3d.com/Packages/com.unity.addressables@4.1//changelog/CHANGELOG.html)

        - com.unity.services.cloudcode: [2.10.4](https://x/a) to [3.0.0](https://x/b)

        **Packages added**

        - [com.unity.external-dependency-manager@2.1.1](https://docs.unity3d.com/Packages/com.unity.external-dependency-manager@2.1//changelog/CHANGELOG.html)

        **Packages deprecated**

        - com.unity.2d.pixel-perfect - "The 2D Pixel Perfect package is deprecated."

        ### Preview of Final 6000.7.0b8 Release Notes

        #### Features

        - Preview: This cumulative entry must be ignored.

        #### Fixes

        - Preview: Another cumulative fix that must be ignored.

        #### Preview of Final 6000.7.0b8 Package changes

        - com.unity.ignored: [1.0.0](https://x) to [2.0.0](https://x)
        """;

    public const string Lts = """
        ### Known Issues in 6000.3.28f1

        - Android:  Back gesture/button closes the app
            ([UUM-159613](https://issuetracker.unity.com/api/v1.0/redirects/guid/UUM-159613))

        - Shader System: Player stops responding
            ([UUM-156125](https://issuetracker.unity.com/api/v1.0/redirects/guid/UUM-156125))



        ### 6000.3.28f1 Release Notes

        #### Improvements

        - License: Updated the Licensing Client to v1.19.0, which starts faster.



        #### API Changes

        - iOS: Added: Added iPhone 18 Pro device generation enums.



        #### Fixes

        - 2D: Fixed shadows from a Collider 2D \(for example a Tilemap Collider 2D\) flickering.
            ([UUM-147033](https://issuetracker.unity.com/api/v1.0/redirects/guid/UUM-147033))

        - Android: Fixed a crash in CreateNewStateMachineBehaviour.
            ([UUM-150813](https://issuetracker.unity.com/api/v1.0/redirects/guid/UUM-150813))

        - Animation: Fixed a crash in HumanTrait.GetParentBone.
            (UUM-150000)




        #### Package changes in 6000.3.28f1

        #### Packages updated

        - com.unity.collab-proxy: [2.13.6](https://x/a) to [2.14.0](https://x/b)

        - com.unity.probuilder: [6.1.3](https://x/a) to [6.1.4](https://x/b)
        """;

    public static UnityRelease Release(string version, UnityStream stream, DateTimeOffset? date = null, bool recommended = false) =>
        new(version, stream, date ?? new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero), $"https://storage.googleapis.com/notes/{version}.md", recommended);

    /// <summary>A beta-sized delta: many entries over many areas, to exercise the layout budgets.</summary>
    public static ReleaseNotes BigBetaNotes(string since = "6000.7.0b7")
    {
        var areas = Enumerable.Range(1, 40).Select(i => $"Area {i:00}").ToArray();
        NoteSection Section(string name, int count, int areaCount) => new(name,
            Enumerable.Range(0, count)
                .Select(i => new NoteEntry(areas[i % areaCount], $"{name} entry {i} describing something that changed in the editor in some detail."))
                .ToList());

        var packages = Enumerable.Range(0, 25)
            .Select(i => new PackageChange($"com.unity.package{i:00}", PackageChangeKind.Updated, "1.0.0", "1.1.0"))
            .ToList();

        return new ReleaseNotes(
            since,
            [Section("Features", 1, 1), Section("Improvements", 10, 6), Section("API Changes", 12, 8), Section("Changes", 13, 8), Section("Fixes", 253, 40)],
            packages,
            new KnownIssues(7, 2));
    }
}
