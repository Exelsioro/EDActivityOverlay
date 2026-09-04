using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Mining;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class MiningSessionDestinationLinkTests
{
    [Fact]
    public void MatchingRingCapturesAndConfirmsDestination()
    {
        MiningSessionSnapshot session = Session("Lalande 34968", "Lalande 34968 AB 8 A Ring");
        MiningDestinationSnapshot destination = Destination();

        MiningSessionDestinationContext context =
            MiningSessionDestinationLinker.Capture(session, destination);

        Assert.True(context.Available);
        Assert.True(context.Confirmed);
        Assert.Equal("Lalande 34968", context.SystemName);
        Assert.Equal("Lalande 34968 AB 8 A Ring", context.RingName);
        Assert.Equal("Platinum", context.PrimaryCommodityId);
        Assert.Equal(["Platinum", "Osmium"], context.TargetCommodityIds);
        Assert.Equal(3, context.OverlapMultiplier);
        Assert.Equal("Hazardous", context.ResType);
        Assert.Equal("Platinum", context.QualityCommodityId);
        Assert.Equal(22.9, context.MeasuredAverageContentPercent, 3);
        Assert.Equal("E:D Tools test", context.QualitySource);
    }

    [Fact]
    public void UnknownLiveRingCapturesPlannedDestinationUnconfirmedThenConfirms()
    {
        MiningSessionDestinationContext planned =
            MiningSessionDestinationLinker.Capture(
                Session("Lalande 34968", string.Empty),
                Destination());

        Assert.True(planned.Available);
        Assert.False(planned.Confirmed);

        MiningSessionDestinationContext confirmed =
            MiningSessionDestinationLinker.Reconcile(
                Session("Lalande 34968", "Lalande 34968 AB 8 A Ring"),
                planned);

        Assert.True(confirmed.Confirmed);
    }

    [Fact]
    public void KnownDifferentRingDoesNotAttachDestination()
    {
        MiningSessionDestinationContext context =
            MiningSessionDestinationLinker.Capture(
                Session("Lalande 34968", "Lalande 34968 AB 7 A Ring"),
                Destination());

        Assert.False(context.Available);
    }

    [Fact]
    public void RepositoryRoundTripPreservesDestinationContext()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "EDActivityOverlay.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            string databasePath = Path.Combine(directory, "companion.db");
            var repository = new MiningSessionRepository(databasePath);
            DateTimeOffset started = new(2026, 9, 4, 10, 0, 0, TimeSpan.Zero);

            MiningSessionSnapshot session = new(
                Guid.NewGuid(),
                MiningSessionState.Finished,
                started,
                started.AddMinutes(30),
                started.AddMinutes(30),
                MiningSessionEndReason.SupercruiseEntry,
                "Test Cmdr",
                42,
                "Lalande 34968",
                8,
                "Lalande 34968 AB 8",
                "Lalande 34968 AB 8 A Ring",
                1,
                2,
                0,
                100,
                256,
                40,
                [
                    new MiningProspectSnapshot(
                        1,
                        started.AddMinutes(2),
                        "High",
                        100,
                        string.Empty,
                        string.Empty,
                        [
                            new MiningProspectMaterialSnapshot(
                                "platinum",
                                "Platinum",
                                31.4)
                        ])
                ],
                Array.Empty<MiningRefinementSnapshot>())
            {
                RingClass = "Metallic",
                ReserveLevel = "Pristine",
                DestinationContext = new MiningSessionDestinationContext
                {
                    SystemName = "Lalande 34968",
                    BodyName = "AB 8",
                    RingName = "Lalande 34968 AB 8 A Ring",
                    Confirmed = true,
                    PrimaryCommodityId = "Platinum",
                    TargetCommodityIds = ["Platinum", "Osmium"],
                    OverlapMultiplier = 3,
                    ResType = "Hazardous",
                    QualityCommodityId = "Platinum",
                    MeasuredAverageContentPercent = 22.9,
                    QualitySource = "E:D Tools test",
                    SelectedUtc = started.AddHours(-1)
                }
            };

            repository.Save(session);
            MiningSessionSnapshot loaded = Assert.Single(repository.LoadRecent());

            Assert.True(loaded.DestinationContext.Available);
            Assert.True(loaded.DestinationContext.Confirmed);
            Assert.Equal("AB 8", loaded.DestinationContext.BodyName);
            Assert.Equal(3, loaded.DestinationContext.OverlapMultiplier);
            Assert.Equal("Hazardous", loaded.DestinationContext.ResType);
            Assert.Equal(
                ["Platinum", "Osmium"],
                loaded.DestinationContext.TargetCommodityIds);
            Assert.Equal(
                22.9,
                loaded.DestinationContext.MeasuredAverageContentPercent,
                3);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static MiningSessionSnapshot Session(
        string system,
        string ring) =>
        new(
            Guid.NewGuid(),
            MiningSessionState.Active,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            null,
            MiningSessionEndReason.None,
            "Test Cmdr",
            42,
            system,
            8,
            "Lalande 34968 AB 8",
            ring,
            1,
            0,
            0,
            0,
            256,
            40,
            Array.Empty<MiningProspectSnapshot>(),
            Array.Empty<MiningRefinementSnapshot>());

    private static MiningDestinationSnapshot Destination() =>
        new()
        {
            SystemName = "Lalande 34968",
            BodyName = "AB 8",
            RingDisplayName = "A Ring",
            RingName = "Lalande 34968 AB 8 A Ring",
            RingClass = "Metallic",
            ReserveLevel = "Pristine",
            PrimaryCommodityId = "Platinum",
            TargetCommodityIds = ["Platinum", "Osmium"],
            OverlapMultiplier = 3,
            ResType = MiningResSiteType.Hazardous,
            QualityCommodityId = "Platinum",
            MeasuredAverageContentPercent = 22.9,
            QualitySource = "E:D Tools test",
            SelectedUtc = new DateTimeOffset(
                2026,
                9,
                4,
                9,
                0,
                0,
                TimeSpan.Zero)
        };
}
