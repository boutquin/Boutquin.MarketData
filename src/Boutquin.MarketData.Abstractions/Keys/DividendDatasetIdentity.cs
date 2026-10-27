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

using System.Globalization;

using Boutquin.MarketData.Abstractions.ReferenceData;

namespace Boutquin.MarketData.Abstractions.Keys;

internal static class DividendDatasetIdentity
{
    internal static string EncodeTicker(string ticker) =>
        ticker.Replace("/", "//", StringComparison.Ordinal);

    internal static string EncodeSymbolList(IEnumerable<string> tickers)
    {
        var ordered = tickers
            .OrderBy(ticker => ticker, StringComparer.OrdinalIgnoreCase)
            .ThenBy(ticker => ticker, StringComparer.Ordinal)
            .ToArray();

        if (ordered.Length == 1 && !ordered[0].Contains('_', StringComparison.Ordinal))
        {
            return EncodeTicker(ordered[0]);
        }

        return $"_D{ordered.Length}_{string.Join("_", ordered.Select(EncodeUtf16))}";
    }

    internal static string EncodeTickerAndExchange(string ticker, ExchangeCode? exchange)
    {
        var encodedTicker = EncodeTicker(ticker);
        return exchange is null ? encodedTicker : $"{encodedTicker}/{exchange.Value}";
    }

    private static string EncodeUtf16(string ticker) =>
        string.Concat(
            ticker.Select(character => ((int)character).ToString("X4", CultureInfo.InvariantCulture)));
}
