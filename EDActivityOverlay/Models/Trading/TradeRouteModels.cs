using System;

namespace EDActivityOverlay.Models.Trading
{
public class TradeLeg
    {
        public Commodity SellCommodity { get; set; } = new Commodity();
        public Commodity BuyCommodity { get; set; } = new Commodity();
        public int ProfitPerUnit { get; set; }
        public string LastUpdate { get; set; } = "";
    }

    public class TradeRoute
    {
        public CardHeader CardHeader { get; set; } = new CardHeader();
        public TradeLeg FirstRoute { get; set; } = new TradeLeg();
        public int CargoCapacity { get; set; } = 1; 

        // Round trip properties
        public bool IsRoundTrip { get; set; } = false;
        public TradeLeg? SecondRoute { get; set; }
        public double RouteDistance { get; set; }

        public string LastUpdate { get; set; } = "";    
        public int TotalProfitPerTrip { get; set; }
        public double TotalRouteDistance => IsRoundTrip && SecondRoute != null ? RouteDistance * 2 : RouteDistance;

    }

    public class CardHeader
    {
        public Station FromStation { get; set; } = new Station();
        public Station ToStation { get; set; } = new Station();
    }
public class Station
    {
        public string Name { get; set; } = "";
        public string System { get; set; } = "";
        public double DistanceFromStar { get; set; }
        
        // New properties
        public string StationType { get; set; } = "";
        public string LandingPadSize { get; set; } = "";
        public double StationDistanceLs { get; set; }
        public string LastUpdated { get; set; } = "";
        public string StationIconKey { get; set; } = "";
    }

    public class Commodity
    {
        public string Name { get; set; } = "";
        public int Price { get; set; }
        public string Supply { get; set; } = "";
        public string Demand { get; set; } = "";
        
        public bool IsOdysseyOnly { get; set; }
    }
    public class TradeRouteSearchParams
    {
        public string NearStarSystem { get; set; } = "";
        public int CargoCapacity { get; set; }
        public string MaxRouteDistance { get; set; } = "";
        public string MaxPriceAge { get; set; } = "";
        public int MinLandingPad { get; set; } = 0;
        public int MaxStationDistance { get; set; } = 0;
        public int UseSurfaceStations { get; set; } = 0;
        public int SourceStationPower { get; set; } = 0;
        public int TargetStationPower { get; set; } = 0;
        public int MinSupply { get; set; } = 0;
        public int MinDemand { get; set; } = 0;
        public int OrderBy { get; set; } = 0;
        
        public bool IncludeRoundTrips { get; set; }
        public bool DisplayPowerplayBonuses { get; set; }
    }
}
