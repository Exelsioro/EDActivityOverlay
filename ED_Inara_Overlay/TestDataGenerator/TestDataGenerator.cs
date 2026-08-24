using System;
using System.Collections.Generic;
using ED_Inara_Overlay.Models.Trading;

namespace ED_Inara_Overlay
{
    public static class TestDataGenerator
    {
        public static List<TradeRoute> GenerateTestData()
        {
            var routes = new List<TradeRoute>();

            // Create single-leg routes
            routes.Add(CreateSingleLegRoute(
                "Sol", "Dahan", "Ruthenium", 
                buyPrice: 12000, sellPrice: 15000, 
                distance: 8.5, supply: "High", demand: "Medium"
            ));

            routes.Add(CreateSingleLegRoute(
                "Alpha Centauri", "Wolf 359", "Palladium", 
                buyPrice: 8000, sellPrice: 11000, 
                distance: 12.3, supply: "Medium", demand: "High"
            ));

            routes.Add(CreateSingleLegRoute(
                "Sirius", "Barnard's Star", "Painite", 
                buyPrice: 50000, sellPrice: 55000, 
                distance: 15.7, supply: "Low", demand: "High"
            ));

            // Create round-trip routes
            routes.Add(CreateRoundTripRoute(
                "Sol", "Dahan", "Ruthenium", 
                buyPrice1: 12000, sellPrice1: 15000, 
                distance1: 8.5, supply1: "High", demand1: "Medium",
                
                "Dahan", "Sol", "Praseodymium", 
                buyPrice2: 7000, sellPrice2: 9500, 
                distance2: 8.5, supply2: "Medium", demand2: "High"
            ));

            routes.Add(CreateRoundTripRoute(
                "Alpha Centauri", "Wolf 359", "Palladium", 
                buyPrice1: 8000, sellPrice1: 11000, 
                distance1: 12.3, supply1: "Medium", demand1: "High",
                
                "Wolf 359", "Alpha Centauri", "Bertrandite", 
                buyPrice2: 2500, sellPrice2: 3200, 
                distance2: 12.3, supply2: "High", demand2: "Medium"
            ));

            routes.Add(CreateRoundTripRoute(
                "Sirius", "Barnard's Star", "Painite", 
                buyPrice1: 50000, sellPrice1: 55000, 
                distance1: 15.7, supply1: "Low", demand1: "High",
                
                "Barnard's Star", "Sirius", "Lithium Hydroxide", 
                buyPrice2: 1200, sellPrice2: 1800, 
                distance2: 15.7, supply2: "Medium", demand2: "High"
            ));

            return routes;
        }

        private static TradeRoute CreateSingleLegRoute(
            string fromSystem, string toSystem, string commodity,
            int buyPrice, int sellPrice, double distance,
            string supply, string demand)
        {
            return new TradeRoute
            {
                IsRoundTrip = false,
                CardHeader = new CardHeader
                {
                    FromStation = new Station
                    {
                        Name = $"{fromSystem} Station",
                        System = fromSystem,
                        StationType = "Coriolis Starport",
                        LandingPadSize = "Large",
                        StationDistanceLs = 150,
                        LastUpdated = DateTime.Now.AddMinutes(-Random.Shared.Next(5, 120)).ToString("yyyy-MM-dd HH:mm:ss")
                    },
                    ToStation = new Station
                    {
                        Name = $"{toSystem} Orbital",
                        System = toSystem,
                        StationType = "Orbis Starport",
                        LandingPadSize = "Large",
                        StationDistanceLs = 250,
                        LastUpdated = DateTime.Now.AddMinutes(-Random.Shared.Next(5, 120)).ToString("yyyy-MM-dd HH:mm:ss")
                    }
                },
                FirstRoute = new TradeLeg
                {
                    BuyCommodity = new Commodity
                    {
                        Name = commodity,
                        Price = buyPrice,
                        Supply = supply
                    },
                    SellCommodity = new Commodity
                    {
                        Name = commodity,
                        Price = sellPrice,
                        Demand = demand
                    },
                    ProfitPerUnit = sellPrice - buyPrice,
                    //RouteDistance = distance,
                    LastUpdate = DateTime.Now.AddMinutes(-Random.Shared.Next(5, 120)).ToString("yyyy-MM-dd HH:mm:ss")
                },
                LastUpdate = DateTime.Now.AddMinutes(-Random.Shared.Next(5, 120)).ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private static TradeRoute CreateRoundTripRoute(
            string fromSystem1, string toSystem1, string commodity1,
            int buyPrice1, int sellPrice1, double distance1,
            string supply1, string demand1,
            
            string fromSystem2, string toSystem2, string commodity2,
            int buyPrice2, int sellPrice2, double distance2,
            string supply2, string demand2)
        {
            return new TradeRoute
            {
                IsRoundTrip = true,
                CardHeader = new CardHeader
                {
                    FromStation = new Station
                    {
                        Name = $"{fromSystem1} Station",
                        System = fromSystem1,
                        StationType = "Coriolis Starport",
                        LandingPadSize = "Large",
                        StationDistanceLs = 150,
                        LastUpdated = DateTime.Now.AddMinutes(-Random.Shared.Next(5, 120)).ToString("yyyy-MM-dd HH:mm:ss")
                    },
                    ToStation = new Station
                    {
                        Name = $"{toSystem1} Orbital",
                        System = toSystem1,
                        StationType = "Orbis Starport",
                        LandingPadSize = "Large",
                        StationDistanceLs = 250,
                        LastUpdated = DateTime.Now.AddMinutes(-Random.Shared.Next(5, 120)).ToString("yyyy-MM-dd HH:mm:ss")
                    }
                },
                FirstRoute = new TradeLeg
                {
                    BuyCommodity = new Commodity
                    {
                        Name = commodity1,
                        Price = buyPrice1,
                        Supply = supply1
                    },
                    SellCommodity = new Commodity
                    {
                        Name = commodity1,
                        Price = sellPrice1,
                        Demand = demand1
                    },
                    ProfitPerUnit = sellPrice1 - buyPrice1,
                    //RouteDistance = distance1,
                    LastUpdate = DateTime.Now.AddMinutes(-Random.Shared.Next(5, 120)).ToString("yyyy-MM-dd HH:mm:ss")
                },
                SecondRoute = new TradeLeg
                {
                    BuyCommodity = new Commodity
                    {
                        Name = commodity2,
                        Price = buyPrice2,
                        Supply = supply2
                    },
                    SellCommodity = new Commodity
                    {
                        Name = commodity2,
                        Price = sellPrice2,
                        Demand = demand2
                    },
                    ProfitPerUnit = sellPrice2 - buyPrice2,
                    //RouteDistance = distance2,
                    LastUpdate = DateTime.Now.AddMinutes(-Random.Shared.Next(5, 120)).ToString("yyyy-MM-dd HH:mm:ss")
                },
                LastUpdate = DateTime.Now.AddMinutes(-Random.Shared.Next(5, 120)).ToString("yyyy-MM-dd HH:mm:ss")
            };
        }
    }
}
