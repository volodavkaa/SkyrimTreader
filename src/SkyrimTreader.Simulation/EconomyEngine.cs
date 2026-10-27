namespace SkyrimTreader.Simulation;

public sealed class EconomyEngine
{
    private readonly EconomyConfig _config;
    public SimulationState State { get; }

    public EconomyEngine(EconomyConfig config, SimulationState? state = null, ulong seed = 1)
    {
        Validate(config);
        _config = config;
        State = state ?? NewState(config, seed);
        if (State.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported state schema {State.SchemaVersion}.");
        if (State.ConfigFingerprint != EconomyJson.Fingerprint(config))
            throw new InvalidDataException("State was created with a different economy config.");
        ValidateState();
    }

    public static SimulationState NewState(EconomyConfig config, ulong seed = 1)
    {
        Validate(config);
        var state = new SimulationState
        {
            RandomState = seed == 0 ? 1 : seed,
            ConfigFingerprint = EconomyJson.Fingerprint(config)
        };
        foreach (var (regionId, region) in config.Regions)
        {
            var markets = new Dictionary<string, MarketState>();
            foreach (var (commodityId, market) in region.Markets)
                markets[commodityId] = new MarketState { Stock = market.InitialStock };
            state.Regions[regionId] = markets;
        }
        return state;
    }

    public void AdvanceDays(int days)
    {
        if (days < 0 || days > 10000) throw new ArgumentOutOfRangeException(nameof(days));
        for (var i = 0; i < days; i++) AdvanceDay();
    }

    public void AdvanceDay()
    {
        State.Day++;
        ApplyProductionAndConsumption();
        ApplyShocks();
        ResolveShipments();
        PlanShipments();
        UpdatePrices();
    }

    public double Multiplier(string region, string commodity) => State.Regions[region][commodity].Multiplier;

    public int RegionalBaseValue(string region, string commodity) =>
        Math.Max(1, (int)Math.Round(_config.Commodities[commodity].BaseValue * Multiplier(region, commodity)));

    public uint RegionalItemValue(string region, CatalogItem item)
    {
        if (item.Commodity is null || !State.Regions[region].ContainsKey(item.Commodity)) return item.BaseValue;
        return (uint)Math.Clamp(Math.Round(item.BaseValue * Multiplier(region, item.Commodity)), 1, uint.MaxValue);
    }

    public bool ApplyTradeItem(long sequence, string region, CatalogItem item, int netSoldToMerchant)
    {
        if (item.Commodity is null || !State.Regions[region].ContainsKey(item.Commodity))
            return ApplyTrade(sequence, region, null, 0);
        return ApplyTrade(sequence, region, item.Commodity, checked(item.Units * netSoldToMerchant));
    }

    public bool ApplyTrade(long sequence, string region, string? commodity, int netSoldToMerchant)
    {
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (sequence <= State.LastTradeSequence) return false;
        if (sequence != checked(State.LastTradeSequence + 1))
            throw new InvalidDataException($"Trade sequence gap: expected {State.LastTradeSequence + 1}, got {sequence}.");
        if (commodity is null)
        {
            State.LastTradeSequence = sequence;
            return true;
        }
        var market = State.Regions[region][commodity];
        var before = market.Stock;
        market.Stock = checked((int)Math.Clamp((long)market.Stock + netSoldToMerchant, 0, int.MaxValue));
        var appliedDelta = market.Stock - before;
        State.TotalPlayerTradeDelta = checked(State.TotalPlayerTradeDelta + appliedDelta);
        State.LastTradeSequence = sequence;
        State.Journal.Add($"day {State.Day}: player trade #{sequence} {region}/{commodity}: {appliedDelta:+#;-#;0}");
        TrimJournal();
        // Prices are frozen until the next daily tick, including the current barter session.
        return true;
    }

    public bool ReportCaravanLoss(long sequence, long shipmentId, int lostQuantity)
    {
        if (lostQuantity < 1) throw new ArgumentOutOfRangeException(nameof(lostQuantity));
        if (!BeginCaravanEvent(sequence)) return false;
        var shipment = State.Shipments.SingleOrDefault(x => x.Id == shipmentId)
            ?? throw new InvalidDataException($"Unknown shipment {shipmentId}.");
        if (!IsActive(shipment))
        {
            State.LastCaravanEventSequence = sequence;
            return false;
        }
        if (lostQuantity > shipment.RemainingQuantity)
            throw new InvalidDataException($"Loss exceeds cargo in shipment {shipmentId}.");
        shipment.RemainingQuantity -= lostQuantity;
        State.TotalLost = checked(State.TotalLost + lostQuantity);
        shipment.EventResolved = true;
        shipment.Status = shipment.RemainingQuantity == 0 ? ShipmentStatus.Destroyed : ShipmentStatus.PartiallyLost;
        State.LastCaravanEventSequence = sequence;
        State.Journal.Add($"day {State.Day}: caravan event #{sequence} shipment {shipmentId} lost {lostQuantity}");
        TrimJournal();
        return true;
    }

    public bool ReportCaravanArrival(long sequence, long shipmentId)
    {
        if (!BeginCaravanEvent(sequence)) return false;
        var shipment = State.Shipments.SingleOrDefault(x => x.Id == shipmentId)
            ?? throw new InvalidDataException($"Unknown shipment {shipmentId}.");
        if (!IsActive(shipment))
        {
            State.LastCaravanEventSequence = sequence;
            return false;
        }
        if ((State.RouteBlockedUntilDay.TryGetValue(shipment.RouteId, out var blockedUntil) &&
             State.Day <= blockedUntil) ||
            (shipment.Status == ShipmentStatus.Delayed && State.Day < shipment.ArrivalDay))
        {
            State.LastCaravanEventSequence = sequence;
            return false;
        }
        var destination = State.Regions[shipment.To][shipment.Commodity];
        destination.Stock = checked(destination.Stock + shipment.RemainingQuantity);
        shipment.EventResolved = true;
        shipment.Status = ShipmentStatus.Arrived;
        State.LastCaravanEventSequence = sequence;
        State.Journal.Add($"day {State.Day}: caravan event #{sequence} shipment {shipmentId} arrived with {shipment.RemainingQuantity} {shipment.Commodity}");
        TrimJournal();
        return true;
    }

    public VisibleCaravanPlan? PlanVisibleCaravan(string playerRegion, bool visibleEnabled = true,
        IReadOnlySet<string>? permittedRoutes = null, IReadOnlySet<string>? permittedCommodities = null)
    {
        if (!_config.Regions.ContainsKey(playerRegion)) throw new ArgumentException("Unknown region.", nameof(playerRegion));
        if (State.VisibleCaravan is { } binding)
        {
            var shipment = State.Shipments.Single(x => x.Id == binding.ShipmentId);
            return ToVisiblePlan(shipment, binding.Region, binding.LeaderFormId,
                visibleEnabled && IsActive(shipment) && (shipment.From == playerRegion || shipment.To == playerRegion) &&
                (permittedRoutes is null || permittedRoutes.Contains(shipment.RouteId)) &&
                (permittedCommodities is null || permittedCommodities.Contains(shipment.Commodity))
                    ? VisibleCaravanAction.Keep : VisibleCaravanAction.Retire, binding);
        }
        if (!visibleEnabled) return null;
        var candidate = State.Shipments
            .Where(x => IsActive(x) && (x.From == playerRegion || x.To == playerRegion) &&
                (permittedRoutes is null || permittedRoutes.Contains(x.RouteId)) &&
                (permittedCommodities is null || permittedCommodities.Contains(x.Commodity)) &&
                !State.RetiredVisibleShipments.Contains(x.Id))
            .OrderBy(x => x.Id).FirstOrDefault();
        return candidate is null ? null : ToVisiblePlan(candidate, playerRegion, 0, VisibleCaravanAction.Spawn);
    }

    public bool ConfirmVisibleCaravanSpawn(long shipmentId, string region, uint leaderFormId,
        IReadOnlySet<string>? permittedRoutes = null, IReadOnlySet<string>? permittedCommodities = null)
    {
        if (leaderFormId == 0) throw new ArgumentOutOfRangeException(nameof(leaderFormId));
        if (State.VisibleCaravan is { } binding)
        {
            if (binding.ShipmentId == shipmentId && binding.Region == region && binding.LeaderFormId == leaderFormId)
                return false;
            throw new InvalidDataException("Another visible caravan is already bound.");
        }
        var plan = PlanVisibleCaravan(region, permittedRoutes: permittedRoutes,
            permittedCommodities: permittedCommodities);
        if (plan is null || plan.Action != VisibleCaravanAction.Spawn || plan.ShipmentId != shipmentId)
            throw new InvalidDataException("Shipment is not the current visible caravan candidate.");
        State.VisibleCaravan = new VisibleCaravanBinding
        {
            ShipmentId = shipmentId, Region = region, LeaderFormId = leaderFormId, SpawnDay = State.Day
        };
        return true;
    }

    public bool ReleaseVisibleCaravan(long shipmentId, uint leaderFormId)
    {
        if (State.VisibleCaravan is null)
        {
            if (State.RetiredVisibleShipments.Contains(shipmentId)) return false;
            throw new InvalidDataException("No visible caravan is bound.");
        }
        if (State.VisibleCaravan.ShipmentId != shipmentId || State.VisibleCaravan.LeaderFormId != leaderFormId)
            throw new InvalidDataException("Visible caravan binding does not match.");
        State.VisibleCaravan = null;
        State.RetiredVisibleShipments.Add(shipmentId);
        return true;
    }

    public bool ConfirmPhysicalCaravanGroup(long shipmentId, string region, uint leaderFormId,
        uint packFormId, uint guardFormId, IReadOnlySet<string> permittedRoutes,
        IReadOnlySet<string> permittedCommodities)
    {
        if (leaderFormId == 0 || packFormId == 0 || guardFormId == 0 ||
            leaderFormId == packFormId || leaderFormId == guardFormId || packFormId == guardFormId)
            throw new ArgumentException("A physical caravan needs three distinct actor references.");
        var applied = ConfirmVisibleCaravanSpawn(shipmentId, region, leaderFormId,
            permittedRoutes, permittedCommodities);
        var binding = State.VisibleCaravan!;
        if (!applied)
        {
            if (binding.PackFormId == packFormId && binding.GuardFormId == guardFormId) return false;
            throw new InvalidDataException("Existing group actors do not match.");
        }
        binding.PackFormId = packFormId;
        binding.GuardFormId = guardFormId;
        return true;
    }

    public bool UpdateVisibleWaypoint(long shipmentId, uint leaderFormId, int waypointIndex, int anchorCount)
    {
        if (anchorCount < 2 || waypointIndex < 0 || waypointIndex >= anchorCount)
            throw new ArgumentOutOfRangeException(nameof(waypointIndex));
        var binding = State.VisibleCaravan;
        if (binding is null || binding.ShipmentId != shipmentId || binding.LeaderFormId != leaderFormId ||
            binding.PackFormId == 0 || binding.GuardFormId == 0)
            throw new InvalidDataException("Physical group binding does not match.");
        if (waypointIndex < binding.WaypointIndex)
            throw new InvalidDataException("Visible caravan waypoint cannot move backwards.");
        if (waypointIndex == binding.WaypointIndex) return false;
        binding.WaypointIndex = waypointIndex;
        return true;
    }

    public bool RebindPhysicalCaravanGroup(long shipmentId, uint oldLeaderFormId,
        uint leaderFormId, uint packFormId, uint guardFormId)
    {
        var binding = State.VisibleCaravan;
        if (binding is null || binding.ShipmentId != shipmentId || binding.LeaderFormId != oldLeaderFormId ||
            binding.PackFormId == 0 || binding.GuardFormId == 0)
            throw new InvalidDataException("Physical group binding does not match.");
        if (leaderFormId == 0 || packFormId == 0 || guardFormId == 0 ||
            leaderFormId == packFormId || leaderFormId == guardFormId || packFormId == guardFormId)
            throw new ArgumentException("Resolved actors must have distinct FormIDs.");
        if (binding.LeaderFormId == leaderFormId && binding.PackFormId == packFormId &&
            binding.GuardFormId == guardFormId) return false;
        binding.LeaderFormId = leaderFormId;
        binding.PackFormId = packFormId;
        binding.GuardFormId = guardFormId;
        return true;
    }

    private static VisibleCaravanPlan ToVisiblePlan(Shipment shipment, string region, uint leaderFormId,
        VisibleCaravanAction action, VisibleCaravanBinding? binding = null) => new()
    {
        ShipmentId = shipment.Id, RouteId = shipment.RouteId, From = shipment.From, To = shipment.To,
        Commodity = shipment.Commodity, RemainingQuantity = shipment.RemainingQuantity,
        ArrivalDay = shipment.ArrivalDay, Region = region, LeaderFormId = leaderFormId,
        PackFormId = binding?.PackFormId ?? 0, GuardFormId = binding?.GuardFormId ?? 0,
        WaypointIndex = binding?.WaypointIndex ?? 0, Action = action
    };

    private bool BeginCaravanEvent(long sequence)
    {
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (sequence <= State.LastCaravanEventSequence) return false;
        if (sequence != checked(State.LastCaravanEventSequence + 1))
            throw new InvalidDataException($"Caravan event sequence gap: expected {State.LastCaravanEventSequence + 1}, got {sequence}.");
        return true;
    }

    private static bool IsActive(Shipment shipment) =>
        shipment.Status is ShipmentStatus.InTransit or ShipmentStatus.Delayed or ShipmentStatus.PartiallyLost;

    public long TotalPhysicalQuantity() =>
        State.Regions.Values.SelectMany(x => x.Values).Sum(x => (long)x.Stock) +
        State.Shipments.Where(x => x.Status is ShipmentStatus.InTransit or ShipmentStatus.Delayed or ShipmentStatus.PartiallyLost)
            .Sum(x => (long)x.RemainingQuantity);

    private void ApplyProductionAndConsumption()
    {
        foreach (var (regionId, region) in _config.Regions.OrderBy(x => x.Key, StringComparer.Ordinal))
        foreach (var (commodityId, setup) in region.Markets.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var market = State.Regions[regionId][commodityId];
            market.Stock = checked(market.Stock + setup.DailyProduction);
            State.TotalProduced += setup.DailyProduction;
            var consumed = Math.Min(market.Stock, setup.DailyConsumption);
            market.Stock -= consumed;
            State.TotalConsumed += consumed;
            var surplus = Math.Max(0, market.Stock - setup.TargetStock);
            var spoiled = (int)Math.Floor(surplus * _config.Commodities[commodityId].SurplusSpoilageRate);
            market.Stock -= spoiled;
            State.TotalSpoiled += spoiled;
        }
    }

    private void ApplyShocks()
    {
        foreach (var shock in _config.Shocks.Where(x => x.Day == State.Day).OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            if (!State.AppliedShocks.Add(shock.Id)) continue;
            if (shock.StockDelta != 0)
            {
                var market = State.Regions[shock.Region][shock.Commodity];
                var before = market.Stock;
                market.Stock = checked((int)Math.Clamp((long)before + shock.StockDelta, 0, int.MaxValue));
                State.TotalShockDelta += market.Stock - before;
            }
            if (shock.BlockRoute is not null)
                State.RouteBlockedUntilDay[shock.BlockRoute] = State.Day + shock.BlockDays;
            State.Journal.Add($"day {State.Day}: shock {shock.Id}");
        }
        TrimJournal();
    }

    private void ResolveShipments()
    {
        foreach (var shipment in State.Shipments.Where(x => x.Status is ShipmentStatus.InTransit or ShipmentStatus.Delayed or ShipmentStatus.PartiallyLost).OrderBy(x => x.Id))
        {
            var route = _config.Routes.Single(x => x.Id == shipment.RouteId);
            if (State.RouteBlockedUntilDay.TryGetValue(route.Id, out var blockedUntil) && State.Day <= blockedUntil)
            {
                if (shipment.ArrivalDay <= State.Day) shipment.ArrivalDay = State.Day + 1;
                shipment.Status = ShipmentStatus.Delayed;
                continue;
            }
            if (!shipment.EventResolved)
            {
                shipment.EventResolved = true;
                var roll = NextRandom();
                if (roll < route.Risk / 4)
                {
                    State.TotalLost += shipment.RemainingQuantity;
                    shipment.RemainingQuantity = 0;
                    shipment.Status = ShipmentStatus.Destroyed;
                    State.Journal.Add($"day {State.Day}: shipment {shipment.Id} destroyed");
                    continue;
                }
                if (roll < route.Risk / 2)
                {
                    var lost = Math.Max(1, shipment.RemainingQuantity / 4);
                    shipment.RemainingQuantity -= lost;
                    State.TotalLost += lost;
                    shipment.Status = ShipmentStatus.PartiallyLost;
                    State.Journal.Add($"day {State.Day}: shipment {shipment.Id} lost {lost}");
                }
                else if (roll < route.Risk)
                {
                    shipment.ArrivalDay++;
                    shipment.Status = ShipmentStatus.Delayed;
                    State.Journal.Add($"day {State.Day}: shipment {shipment.Id} delayed");
                }
            }
            if (State.Day < shipment.ArrivalDay) continue;
            var destination = State.Regions[shipment.To][shipment.Commodity];
            destination.Stock = checked(destination.Stock + shipment.RemainingQuantity);
            shipment.Status = ShipmentStatus.Arrived;
            State.Journal.Add($"day {State.Day}: shipment {shipment.Id} arrived with {shipment.RemainingQuantity} {shipment.Commodity}");
        }
        TrimJournal();
    }

    private void PlanShipments()
    {
        foreach (var route in _config.Routes.OrderBy(x => x.Id, StringComparer.Ordinal))
        {
            if (State.RouteBlockedUntilDay.TryGetValue(route.Id, out var blockedUntil) && State.Day <= blockedUntil) continue;
            var source = _config.Regions[route.From];
            var destination = _config.Regions[route.To];
            var capacity = route.Capacity;
            foreach (var commodity in source.Markets.Keys.Intersect(destination.Markets.Keys).OrderBy(x => x, StringComparer.Ordinal))
            {
                if (capacity == 0) break;
                var sourceRule = source.Markets[commodity];
                var destRule = destination.Markets[commodity];
                var sourceStock = State.Regions[route.From][commodity];
                var destStock = State.Regions[route.To][commodity];
                // Hubs may forward cargo before reaching their target stock, but never below their explicit reserve.
                var surplus = Math.Max(0, sourceStock.Stock - sourceRule.ReserveStock);
                var incoming = State.Shipments
                    .Where(x => x.To == route.To && x.Commodity == commodity && x.Status is ShipmentStatus.InTransit or ShipmentStatus.Delayed or ShipmentStatus.PartiallyLost)
                    .Sum(x => x.RemainingQuantity);
                var predictedStock = Math.Max(0L, (long)destStock.Stock + incoming +
                    (long)route.TravelDays * (destRule.DailyProduction - destRule.DailyConsumption));
                var deficit = Math.Max(0L, (long)destRule.TargetStock - predictedStock);
                var quantity = (int)Math.Min(capacity, Math.Min(surplus, deficit));
                if (quantity < _config.MinShipmentQuantity) continue;
                var sourcePrice = TargetPrice(route.From, commodity);
                var destPrice = TargetPrice(route.To, commodity);
                var required = sourcePrice * (1 + route.CostFraction + _config.MinTradeMargin);
                if (destPrice <= required) continue;
                sourceStock.Stock -= quantity;
                capacity -= quantity;
                var shipment = new Shipment
                {
                    Id = State.NextShipmentId++, RouteId = route.Id, From = route.From, To = route.To,
                    Commodity = commodity, SentQuantity = quantity, RemainingQuantity = quantity,
                    DepartureDay = State.Day, ArrivalDay = State.Day + route.TravelDays,
                    Status = ShipmentStatus.InTransit
                };
                State.Shipments.Add(shipment);
                State.Journal.Add($"day {State.Day}: shipment {shipment.Id} {route.From}->{route.To} {quantity} {commodity}");
            }
        }
        TrimJournal();
    }

    private void UpdatePrices()
    {
        foreach (var (regionId, region) in _config.Regions.OrderBy(x => x.Key, StringComparer.Ordinal))
        foreach (var commodity in region.Markets.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            var market = State.Regions[regionId][commodity];
            var target = TargetPrice(regionId, commodity);
            market.Multiplier = Math.Clamp(target,
                Math.Max(_config.MinMultiplier, market.Multiplier - _config.MaxDailyPriceChange),
                Math.Min(_config.MaxMultiplier, market.Multiplier + _config.MaxDailyPriceChange));
        }
    }

    private double TargetPrice(string region, string commodity)
    {
        var rule = _config.Regions[region].Markets[commodity];
        var stock = State.Regions[region][commodity].Stock;
        var coverage = (double)stock / rule.TargetStock;
        var scarcity = Math.Clamp(1 - coverage, -1, 1);
        return Math.Clamp(1 + _config.PriceSensitivity * scarcity, _config.MinMultiplier, _config.MaxMultiplier);
    }

    private double NextRandom()
    {
        // xorshift64*: the RNG state is persisted so save/load cannot reroll a caravan event.
        var x = State.RandomState;
        x ^= x >> 12;
        x ^= x << 25;
        x ^= x >> 27;
        State.RandomState = x;
        return ((x * 2685821657736338717UL) >> 11) * (1.0 / (1UL << 53));
    }

    private void TrimJournal()
    {
        if (State.Journal.Count > 300) State.Journal.RemoveRange(0, State.Journal.Count - 300);
    }

    private void ValidateState()
    {
        if (State.Day < 0 || State.RandomState == 0 || State.NextShipmentId < 1 ||
            State.LastTradeSequence < 0 || State.LastCaravanEventSequence < 0 ||
            State.TotalProduced < 0 || State.TotalConsumed < 0 || State.TotalLost < 0 || State.TotalSpoiled < 0 ||
            State.Regions is null || State.Shipments is null || State.RouteBlockedUntilDay is null ||
            State.AppliedShocks is null || State.Journal is null)
            throw new InvalidDataException("Invalid simulation state header.");

        foreach (var (regionId, region) in _config.Regions)
        foreach (var commodity in region.Markets.Keys)
        {
            if (!State.Regions.TryGetValue(regionId, out var markets) || markets is null ||
                !markets.TryGetValue(commodity, out var market) || market is null || market.Stock < 0 ||
                !double.IsFinite(market.Multiplier) ||
                market.Multiplier < _config.MinMultiplier || market.Multiplier > _config.MaxMultiplier)
                throw new InvalidDataException($"Invalid state market {regionId}/{commodity}.");
        }

        var routeById = _config.Routes.ToDictionary(x => x.Id);
        var shipmentIds = new HashSet<long>();
        foreach (var shipment in State.Shipments)
        {
            if (shipment is null || shipment.Id < 1 || !shipmentIds.Add(shipment.Id) ||
                shipment.Id >= State.NextShipmentId ||
                !routeById.TryGetValue(shipment.RouteId, out var route) ||
                shipment.From != route.From || shipment.To != route.To ||
                !_config.Regions[route.From].Markets.ContainsKey(shipment.Commodity) ||
                !_config.Regions[route.To].Markets.ContainsKey(shipment.Commodity) ||
                shipment.SentQuantity < _config.MinShipmentQuantity || shipment.SentQuantity > route.Capacity ||
                shipment.RemainingQuantity < 0 || shipment.RemainingQuantity > shipment.SentQuantity ||
                shipment.DepartureDay < 1 || shipment.DepartureDay > State.Day ||
                shipment.ArrivalDay < shipment.DepartureDay + route.TravelDays ||
                !Enum.IsDefined(shipment.Status) ||
                (shipment.Status == ShipmentStatus.Destroyed && shipment.RemainingQuantity != 0) ||
                (IsActive(shipment) && shipment.RemainingQuantity == 0))
                throw new InvalidDataException($"Invalid shipment {shipment?.Id} in state.");
        }

        foreach (var (routeId, untilDay) in State.RouteBlockedUntilDay)
            if (!routeById.ContainsKey(routeId) || untilDay < 0)
                throw new InvalidDataException($"Invalid route block {routeId} in state.");

        if (State.RetiredVisibleShipments is null ||
            State.RetiredVisibleShipments.Any(id => !shipmentIds.Contains(id)))
            throw new InvalidDataException("Invalid retired visible caravan in state.");
        if (State.VisibleCaravan is { } binding &&
            (!shipmentIds.Contains(binding.ShipmentId) || State.RetiredVisibleShipments.Contains(binding.ShipmentId) ||
             !_config.Regions.ContainsKey(binding.Region) || binding.LeaderFormId == 0 ||
             binding.SpawnDay < 0 || binding.SpawnDay > State.Day || binding.WaypointIndex < 0 ||
             (binding.PackFormId == 0) != (binding.GuardFormId == 0) ||
             (binding.PackFormId != 0 && (binding.PackFormId == binding.LeaderFormId ||
                binding.GuardFormId == binding.LeaderFormId || binding.PackFormId == binding.GuardFormId)) ||
             !State.Shipments.Any(x => x.Id == binding.ShipmentId &&
                 (x.From == binding.Region || x.To == binding.Region))))
            throw new InvalidDataException("Invalid visible caravan binding in state.");

        var initial = _config.Regions.Values.Sum(region => region.Markets.Values.Sum(market => (long)market.InitialStock));
        var expected = checked(initial + State.TotalProduced - State.TotalConsumed - State.TotalLost -
            State.TotalSpoiled + State.TotalShockDelta + State.TotalPlayerTradeDelta);
        if (TotalPhysicalQuantity() != expected)
            throw new InvalidDataException("State stock ledger does not balance.");
    }

    public static void Validate(EconomyConfig config)
    {
        if (config.SchemaVersion != 1) throw new InvalidDataException("Unsupported config schema.");
        if (config.Commodities.Count == 0 || config.Regions.Count == 0) throw new InvalidDataException("No commodities or regions.");
        if (!double.IsFinite(config.PriceSensitivity) || !double.IsFinite(config.MinMultiplier) ||
            !double.IsFinite(config.MaxMultiplier) || !double.IsFinite(config.MaxDailyPriceChange) ||
            !double.IsFinite(config.MinTradeMargin) || config.PriceSensitivity < 0 ||
            config.MinMultiplier <= 0 || config.MinMultiplier > 1 || config.MaxMultiplier < 1 ||
            config.MaxDailyPriceChange <= 0 || config.MinTradeMargin < 0 || config.MinShipmentQuantity < 1)
            throw new InvalidDataException("Invalid price settings.");
        foreach (var (id, commodity) in config.Commodities)
            if (string.IsNullOrWhiteSpace(id) || commodity.BaseValue <= 0 ||
                !double.IsFinite(commodity.SurplusSpoilageRate) || commodity.SurplusSpoilageRate is < 0 or > 1)
                throw new InvalidDataException($"Invalid commodity {id}.");
        foreach (var (id, region) in config.Regions)
        {
            if (string.IsNullOrWhiteSpace(id) || region.Markets.Count == 0) throw new InvalidDataException($"Invalid region {id}.");
            foreach (var (commodity, market) in region.Markets)
                if (!config.Commodities.ContainsKey(commodity) || market.InitialStock < 0 || market.TargetStock <= 0 ||
                    market.DailyProduction < 0 || market.DailyConsumption < 0 || market.ReserveStock < 0)
                    throw new InvalidDataException($"Invalid market {id}/{commodity}.");
        }
        var routeIds = new HashSet<string>();
        foreach (var route in config.Routes)
            if (!routeIds.Add(route.Id) || !config.Regions.ContainsKey(route.From) || !config.Regions.ContainsKey(route.To) ||
                route.From == route.To || route.TravelDays < 1 || route.Capacity < 1 ||
                !double.IsFinite(route.CostFraction) || !double.IsFinite(route.Risk) ||
                route.CostFraction < 0 || route.Risk is < 0 or > 1)
                throw new InvalidDataException($"Invalid route {route.Id}.");
        var shockIds = new HashSet<string>();
        foreach (var shock in config.Shocks)
            if (!shockIds.Add(shock.Id) || shock.Day < 1 || !config.Regions.TryGetValue(shock.Region, out var region) ||
                !region.Markets.ContainsKey(shock.Commodity) || (shock.BlockRoute is not null && !routeIds.Contains(shock.BlockRoute)) || shock.BlockDays < 0)
                throw new InvalidDataException($"Invalid shock {shock.Id}.");
    }
}
