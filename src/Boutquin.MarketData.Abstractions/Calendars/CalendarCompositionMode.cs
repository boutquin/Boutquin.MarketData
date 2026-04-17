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

namespace Boutquin.MarketData.Abstractions.Calendars;

/// <summary>
/// Determines how a composite calendar combines its constituent calendars.
/// </summary>
public enum CalendarCompositionMode
{
    /// <summary>
    /// A day is a business day only if <b>all</b> constituent calendars agree it is.
    /// Suitable for settlement calendars where all markets must be open.
    /// </summary>
    All,

    /// <summary>
    /// A day is a business day if <b>any</b> constituent calendar considers it one.
    /// Suitable for trading calendars where at least one market is open.
    /// </summary>
    Any,
}
