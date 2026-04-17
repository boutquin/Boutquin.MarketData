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

using Boutquin.MarketData.Abstractions.Diagnostics;

namespace Boutquin.MarketData.Abstractions.Results;

/// <summary>
/// Represents a data quality issue encountered during fetch or normalization.
/// </summary>
/// <param name="Code">A machine-readable issue code (e.g., <see cref="IssueCode.MissingField"/>, <see cref="IssueCode.StaleData"/>).</param>
/// <param name="Severity">The severity level — <see cref="IssueSeverity.Warning"/> for recoverable gaps, <see cref="IssueSeverity.Error"/> for data the consumer should not trust.</param>
/// <param name="Message">A human-readable description of the issue, including context such as the affected date or field.</param>
/// <remarks>
/// Issues accumulate through the pipeline — adapters, normalizers, and the orchestrator may all
/// contribute entries. The <see cref="Code"/> field is intended for programmatic filtering
/// (e.g., retry on <see cref="IssueCode.RateLimited"/>, skip on <see cref="IssueCode.HolidayGap"/>).
/// </remarks>
public sealed record DataIssue(IssueCode Code, IssueSeverity Severity, string Message);
