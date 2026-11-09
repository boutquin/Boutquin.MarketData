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

namespace Boutquin.MarketData.Abstractions.Requests;

/// <summary>Identifies who computed the adjusted-close series.</summary>
/// <remarks>Values enter persistent keys and provenance. Append members; never renumber them.</remarks>
public enum AdjustedCloseSource
{
    /// <summary>The provider supplied the adjusted close, or no adjustment was applied.</summary>
    ProviderSupplied = 1,
    /// <summary>The adapter computed the adjusted close from provider prices and events.</summary>
    AdapterComputed = 2
}
