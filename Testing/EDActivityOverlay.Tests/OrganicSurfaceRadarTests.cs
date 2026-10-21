using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Linq;
using EDActivityOverlay.Models;
using EDActivityOverlay.Services.Exploration;
using Xunit;

namespace EDActivityOverlay.LayoutTests;

public sealed class OrganicSurfaceRadarTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-19T12:00:00Z");

    [Fact]
    public void SurfaceSampleDoesNotRequireQueueActivationOrDestination()
    {
        OrganicScanProgressSnapshot sample = Sample();
        GameStateSnapshot state = State(sample) with { DestinationBodyId = -1, DestinationName = "" };
        Assert.Same(sample, OrganicSampleNavigation.ResolveActiveSurfaceSample(state));
        state = state with { DestinationBodyId = 99, DestinationName = "Unrelated navigation target" };
        Assert.Same(sample, OrganicSampleNavigation.ResolveActiveSurfaceSample(state));
    }

    [Fact]
    public void TemporaryCoordinateLossKeepsSurfaceContextButDoesNotInventDirection()
    {
        OrganicScanProgressSnapshot sample = Sample();
        GameStateSnapshot state = State(sample) with
        {
            HasSurfacePosition = false, OnFootOnPlanet = true, Latitude = null, Longitude = null
        };
        Assert.Same(sample, OrganicSampleNavigation.ResolveActiveSurfaceSample(state));
        Assert.Null(OrganicSampleNavigation.Assess(state, sample).EscapeBearingDegrees);
    }

    [Fact]
    public void LatestUnfinishedSpeciesIsUsedOnTheActualBody()
    {
        OrganicScanProgressSnapshot first = Sample();
        OrganicScanProgressSnapshot second = first with
        {
            Species = "Bacterium", UpdatedUtc = Start.AddMinutes(1)
        };
        OrganicScanProgressSnapshot complete = first with { Completed = true, UpdatedUtc = Start.AddMinutes(2) };
        GameStateSnapshot state = State(first) with { OrganicProgress = [first, second, complete] };
        Assert.Same(second, OrganicSampleNavigation.ResolveActiveSurfaceSample(state));
    }

    [Theory]
    [InlineData("body")]
    [InlineData("system")]
    [InlineData("commander")]
    [InlineData("space")]
    public void StaleSamplesAreNotShownOutsideTheirSurfaceContext(string change)
    {
        OrganicScanProgressSnapshot sample = Sample();
        GameStateSnapshot state = State(sample);
        state = change switch
        {
            "body" => state with { CurrentBody = "Test 5" },
            "system" => state with { SystemAddress = 43 },
            "commander" => state with { Commander = "Someone else" },
            _ => state with { HasSurfacePosition = false, OnFootOnPlanet = false }
        };
        Assert.Null(OrganicSampleNavigation.ResolveActiveSurfaceSample(state));
    }

    [Fact]
    public void EscapeDirectionLeavesBothOverlappingZonesInsteadOfPointingIntoOtherSample()
    {
        OrganicScanProgressSnapshot sample = Sample() with
        {
            Stage = 2,
            SampleLocations = [new(1, 0, -0.023, Start), new(2, 0, 0.023, Start.AddMinutes(1))]
        };
        OrganicSampleSeparation result = OrganicSampleNavigation.Assess(State(sample), sample);
        Assert.False(result.IsReady);
        Assert.InRange(result.EscapeDistanceMeters!.Value, 290, 310);
        Assert.InRange(result.EscapeBearingDegrees!.Value, 0, 0.01);
        Assert.True(result.EscapeDistanceMeters > 500 - result.NearestSample!.DistanceMeters);
    }

    [Fact]
    public void DirectionDoesNotFlipAtTheMidpointBetweenSamples()
    {
        OrganicScanProgressSnapshot sample = Sample() with
        {
            Stage = 2,
            SampleLocations = [new(1, 0, -0.023, Start), new(2, 0, 0.023, Start.AddMinutes(1))]
        };
        foreach (double longitude in new[] { -0.00000001, 0, 0.00000001 })
        {
            OrganicSampleSeparation result = OrganicSampleNavigation.Assess(
                State(sample) with { Longitude = longitude }, sample);
            Assert.InRange(result.EscapeBearingDegrees!.Value, 0, 0.01);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, -90)]
    [InlineData(180, -180)]
    [InlineData(270, 90)]
    public void WorldBearingIsConvertedToPlayerRelativeTurn(double heading, double expectedTurn)
    {
        OrganicScanProgressSnapshot sample = Sample();
        OrganicSampleSeparation result = OrganicSampleNavigation.Assess(
            State(sample) with { HeadingDegrees = heading }, sample);
        Assert.Equal(expectedTurn, result.EscapeRelativeTurnDegrees!.Value, 4);
    }

    [Fact]
    public void MissingHeadingStillHasDistancesButNotAnInventedForwardArrow()
    {
        OrganicScanProgressSnapshot sample = Sample();
        OrganicSampleSeparation result = OrganicSampleNavigation.Assess(
            State(sample) with { HeadingDegrees = null }, sample);
        Assert.NotNull(result.NearestSample);
        Assert.NotNull(result.EscapeBearingDegrees);
        Assert.Null(result.EscapeRelativeTurnDegrees);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(6_371_000d)]
    public void KnownBodyRadiusIsUsedWhenStatusRadiusIsAbsentOrStale(double? statusRadius)
    {
        OrganicScanProgressSnapshot sample = Sample();
        var body = new ExplorationBodySnapshot(4, "Test 4", "", 0, false, false,
            false, false, 0, [], ExplorationInterest.None) { RadiusMeters = 1_000_000 };
        OrganicSampleSeparation result = OrganicSampleNavigation.Assess(State(sample) with
        {
            Longitude = 0.005, PlanetRadiusMeters = statusRadius, ExplorationBodies = [body]
        }, sample);
        Assert.InRange(result.NearestSample!.DistanceMeters, 87, 88);
    }

    [Theory]
    [InlineData(0, 25, 4)]
    [InlineData(90, 46, 25)]
    [InlineData(180, 25, 46)]
    [InlineData(270, 4, 25)]
    public void RealWpfArrowRotatesInsideRadarAroundItsCentre(double angle, double expectedX, double expectedY)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                XDocument document = XDocument.Load(FindWorkspaceXaml());
                XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
                XElement node = document.Descendants().Single(element =>
                    (string?)element.Attribute(x + "Name") == "SurfaceEscapeArrow");
                var arrow = (System.Windows.Shapes.Path)XamlReader.Parse(node.ToString());
                arrow.Fill = Brushes.Orange;
                var parent = new Grid { Width = 50, Height = 50 };
                parent.Children.Add(arrow);
                parent.Measure(new Size(50, 50));
                parent.Arrange(new Rect(0, 0, 50, 50));
                ((RotateTransform)arrow.RenderTransform).Angle = angle;
                Point tip = arrow.TransformToAncestor(parent).Transform(new Point(25, 4));
                Assert.Equal(expectedX, tip.X, 4);
                Assert.Equal(expectedY, tip.Y, 4);
            }
            catch (Exception error) { failure = error; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "WPF radar layout test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static GameStateSnapshot State(OrganicScanProgressSnapshot sample) => new()
    {
        Commander = "Cmdr", SystemAddress = 42, StarSystem = "Test", CurrentBody = "Test 4",
        Latitude = 0, Longitude = 0, HeadingDegrees = 0, HasSurfacePosition = true,
        PlanetRadiusMeters = 1_000_000, OrganicProgress = [sample]
    };

    private static OrganicScanProgressSnapshot Sample() => new(
        "Cmdr", 42, "Test", 4, "Test 4", "Stratum", "Stratum Tectonicas", "",
        1, false, 500, 0, 0, Start)
    {
        SampleLocations = [new(1, 0, 0, Start)]
    };

    private static string FindWorkspaceXaml()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string file = System.IO.Path.Combine(directory.FullName,
                "EDActivityOverlay", "UserControls", "ExplorationWorkspaceControl.xaml");
            if (File.Exists(file)) return file;
        }
        throw new FileNotFoundException("Exploration workspace XAML not found.");
    }
}
