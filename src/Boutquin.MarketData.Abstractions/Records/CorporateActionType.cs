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

namespace Boutquin.MarketData.Abstractions.Records;

/// <summary>
/// Classifies the type of corporate action affecting a security's price or share count.
/// </summary>
public enum CorporateActionType
{
    /// <summary>
    /// A cash dividend distribution. <see cref="CorporateAction.Amount"/> is the per-share payment.
    /// </summary>
    CashDividend,

    /// <summary>
    /// A forward or reverse stock split. <see cref="CorporateAction.Factor"/> is the split ratio
    /// (e.g., 4.0 for a 4-for-1 split, 0.5 for a 1-for-2 reverse split).
    /// </summary>
    Split,

    /// <summary>
    /// A spin-off distributing shares of a new entity. <see cref="CorporateAction.Factor"/> is the
    /// distribution ratio (shares of new entity per share of parent).
    /// </summary>
    SpinOff,

    /// <summary>
    /// A rights issue offering existing shareholders the right to purchase additional shares.
    /// </summary>
    RightsIssue
}
