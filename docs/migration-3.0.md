# Migrating to MarketData 3.0

MarketData 3.0 makes the price basis explicit. A basis records the adjustments included in adjusted close, who computed it, and the adjustments included in OHLC. This is a breaking caller and adapter contract.

## Price callers

Set `Basis` on every `PriceHistoryRequest`, including test fixtures and imported snapshot requests. There is no default or “any basis” request:

```csharp
var request = new PriceHistoryRequest([new Symbol("SPY")], range)
{
    Basis = new PriceBasis(PriceAdjustment.All,
        AdjustedCloseSource.ProviderSupplied, PriceAdjustment.None)
};
```

Tiingo delivers `all.provider.none`; Twelve Data delivers `all.computed.splits`. With Adapter 3.0, prefer `TiingoPriceBarAdapter.DeliveredPriceBasis` or `TwelveDataPriceBarAdapter.DeliveredPriceBasis` when choosing those contracts. Neither adapter delivers raw bars.

The flags are `None = 0`, `Splits = 1`, `Dividends = 2` and `All = 3`. OHLC adjustments must be a subset of adjusted-close adjustments. Unadjusted close requires `ProviderSupplied`. Undefined flags, undefined sources and the invalid zero-initialized basis are rejected. Enum values are stable and never renumbered.

## Third-party price adapters

Implement `IPriceBasisAdapter` and return the single validated basis from `DeliveredBasis`. Restrict `CanHandle` to requests whose basis equals it. In `FetchAsync`, throw `PriceBasisNotSupportedException(request.Basis, [DeliveredBasis])` before I/O for any other basis. Set `PriceBasis = DeliveredBasis` on every price provenance entry. Tests can use `TestProvenance.ForPriceDelivery(provider, basis)`.

An undeclared adapter is skipped with `Skipped — no declared price basis`. If none declares the requested basis, the pipeline throws `PriceBasisNotSupportedException` before dispatch. A permissive evaluator cannot admit unproven or mismatched price provenance: those deliveries are rejected and the next eligible adapter is tried.

## Caches and rollback

Every price `DatasetKey` ends in `/basis:{Token}`. L1, L2, checkpoint and selected-provider identities therefore change. The price cache cold start refetches each range once; old snapshots are neither read nor overwritten. Keep them for rollback to a 2.0.x binary. Reclaim only the obsolete price entries after rollback is no longer needed. Non-price cache keys and write-only raw-document keys are unchanged.

An unstamped entry under a new L1/L2 key is a cache miss and is overwritten after a valid fetch. A stated mismatch throws `PriceBasisMismatchException` with the requested/stored bases and canonical provider code. Record equality now includes the basis in both requests and provenance.

## Time-series and snapshot reads

`AllowIncrementalPriceHistoryGapFill` stays off by default. Turning it on permits price gap filling only for `PriceBasis.Raw`; all other bases use the whole-window path because their levels are response-anchored. Returns within a response are the contract; levels from separate adjusted responses must not be spliced.

After any registered price key builder, the kernel appends `/{Basis.Token}` to its key's provider. This includes custom builders. Raw series now use `default/none.provider.none` instead of `default`, without changing the Storage key schema. Legacy series remain untouched.

The warm-start importer accepts `Bar` snapshots only when the caller can truthfully set both `Request.Basis = PriceBasis.Raw` and `LegacyL2Snapshot.AttestsRawPriceBasis = true`; other price snapshots throw before append. Existing in-chain adjusted deliveries cannot be imported as raw. Re-fetch those ranges through an adapter declaring the desired basis.

Snapshot price reads state `PriceBasis.Raw`. A gap still throws `SnapshotGapException` first; a stored-range read requesting another basis throws `PriceBasisMismatchException`. Even a fully populated raw store cannot serve a live request when no registered adapter declares raw.

## Failure handling and versions

Both typed basis exceptions leave `IDataPipeline.ExecuteAsync` regardless of `ContinueOnPartitionFailure`. Ordinary adapter failures retain the existing failed-partition issue behavior. Multi-symbol results cannot mix price bases, and the two in-chain providers no longer act as fallback substitutes for one another. Callers choosing availability across bases must make separate requests and handle their level anchors explicitly.

All kernel assemblies use assembly and file version `3.0.0.0`. Storage, Domain and Numerics are unchanged by this feature. No column or basis field is added to `Bar`.

Custom price partitioners must preserve `Basis` in every partition, including a single partition. A rewritten basis is refused before dispatch. They may split symbols while preserving the original range. An adjusted-price partition that changes that date window throws `InvalidOperationException` before adapter dispatch, under both pipeline cache modes. Raw requests may still partition dates. Matching basis stamps alone do not make independently anchored adjusted windows safe to concatenate.
