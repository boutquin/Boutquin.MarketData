// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License").
//  You may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//  See the License for the specific language governing permissions and
//  limitations under the License.
//

using System.Text.Json.Serialization;

namespace Boutquin.MarketData.Abstractions.Requests;

/// <summary>Identifies the adjustments and computation source of a price series.</summary>
/// <remarks>
/// Immutable and safe for concurrent reads. Non-raw levels are anchored to one response:
/// returns within that response are comparable, but separately fetched levels must not be spliced.
/// The zero-initialized struct is invalid; request and provenance initializers reject it.
/// </remarks>
public readonly record struct PriceBasis
{
    /// <summary>Creates a validated price basis without I/O or owned resources.</summary>
    /// <param name="adjustedClose">Actions folded into adjusted close.</param>
    /// <param name="adjustedCloseSource">Who computes adjusted close.</param>
    /// <param name="ohlc">Actions folded into open, high, low and close; a subset of adjusted-close actions.</param>
    /// <exception cref="ArgumentOutOfRangeException">Flags or source are undefined, OHLC is not a subset, or unadjusted close names an adapter computation.</exception>
    [JsonConstructor]
    public PriceBasis(PriceAdjustment adjustedClose, AdjustedCloseSource adjustedCloseSource, PriceAdjustment ohlc)
    {
        if ((adjustedClose & ~PriceAdjustment.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(adjustedClose));
        }
        if (!Enum.IsDefined(adjustedCloseSource))
        {
            throw new ArgumentOutOfRangeException(nameof(adjustedCloseSource));
        }
        if ((ohlc & ~PriceAdjustment.All) != 0 || (ohlc & ~adjustedClose) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ohlc));
        }
        if (adjustedClose == PriceAdjustment.None && adjustedCloseSource != AdjustedCloseSource.ProviderSupplied)
        {
            throw new ArgumentOutOfRangeException(nameof(adjustedCloseSource));
        }
        AdjustedClose = adjustedClose;
        AdjustedCloseSource = adjustedCloseSource;
        Ohlc = ohlc;
    }

    /// <summary>Actions incorporated into adjusted close.</summary>
    public PriceAdjustment AdjustedClose { get; }
    /// <summary>The provider or adapter that computes adjusted close.</summary>
    public AdjustedCloseSource AdjustedCloseSource { get; }
    /// <summary>Actions incorporated into open, high, low and close.</summary>
    public PriceAdjustment Ohlc { get; }
    /// <summary>The unadjusted provider price basis.</summary>
    public static PriceBasis Raw { get; } = new(PriceAdjustment.None, AdjustedCloseSource.ProviderSupplied, PriceAdjustment.None);
    /// <summary>Whether levels may be combined across separately fetched windows.</summary>
    public bool LevelsAreWindowIndependent => AdjustedClose == PriceAdjustment.None && Ohlc == PriceAdjustment.None && AdjustedCloseSource == AdjustedCloseSource.ProviderSupplied;
    /// <summary>The stable, ordinal identity used in dataset and cache keys.</summary>
    /// <exception cref="InvalidOperationException">This is the invalid default struct value.</exception>
    public string Token => AdjustedCloseSource == 0
        ? throw new InvalidOperationException("The default PriceBasis is invalid. Construct an explicit price basis.")
        : $"{AdjustmentToken(AdjustedClose)}.{(AdjustedCloseSource == AdjustedCloseSource.ProviderSupplied ? "provider" : "computed")}.{AdjustmentToken(Ohlc)}";

    private static string AdjustmentToken(PriceAdjustment value) => value switch
    {
        PriceAdjustment.None => "none",
        PriceAdjustment.Splits => "splits",
        PriceAdjustment.Dividends => "dividends",
        PriceAdjustment.All => "all",
        _ => throw new InvalidOperationException("Undefined price adjustment.")
    };
}
