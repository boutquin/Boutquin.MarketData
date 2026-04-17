// Copyright (c) 2026 Pierre G. Boutquin. All rights reserved.
//
//   Licensed under the Apache License, Version 2.0 (the "License").
//   You may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//
//       http://www.apache.org/licenses/LICENSE-2.0
//
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//
//   See the License for the specific language governing permissions and
//   limitations under the License.
//

using System.Text.Json.Serialization;

namespace Boutquin.MarketData.Abstractions.Diagnostics;

/// <summary>
/// Strongly-typed, upper-case machine-readable code carried on <see cref="Results.DataIssue"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IssueCode"/> replaces the earlier bare-<c>string</c> code so that
/// consumers can branch on typed values (<c>issue.Code == IssueCode.StaleData</c>)
/// instead of string literals, while still supporting ad-hoc codes for provider-specific
/// failure modes that have not yet been promoted to the well-known set.
/// </para>
/// <para>
/// The constructor rejects null/whitespace and normalises to upper-case so that
/// <c>new IssueCode("stale_data") == IssueCode.StaleData</c>. The well-known statics
/// are the core vocabulary used by MarketData's own components (coverage classifier,
/// snapshot staleness policy, pipeline partition handler); adapters and consumers are
/// free to construct new codes ad-hoc and, if they become common, promote them to a
/// well-known static in a future release.
/// </para>
/// </remarks>
public readonly record struct IssueCode : IComparable<IssueCode>
{
    /// <summary>
    /// The canonical upper-case code (e.g. <c>"STALE_DATA"</c>, <c>"RATE_LIMITED"</c>).
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Initializes a new <see cref="IssueCode"/> from an arbitrary-cased identifier.
    /// </summary>
    /// <param name="value">
    /// The code text. Must be non-null and contain at least one non-whitespace
    /// character; casing is normalised to upper-case via
    /// <see cref="string.ToUpperInvariant"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="value"/> is <see langword="null"/>, empty, or consists
    /// only of whitespace characters.
    /// </exception>
    [JsonConstructor]
    public IssueCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.ToUpperInvariant();
    }

    /// <summary>
    /// Compares this code with another using ordinal string ordering on the canonical
    /// <see cref="Value"/>.
    /// </summary>
    /// <param name="other">The code to compare against.</param>
    /// <returns>
    /// A negative value if this code precedes <paramref name="other"/> in ordinal order;
    /// zero if equal; positive if this code follows.
    /// </returns>
    public int CompareTo(IssueCode other) =>
        string.CompareOrdinal(Value, other.Value);

    /// <summary>
    /// Returns the canonical upper-case code text.
    /// </summary>
    /// <returns>The value of <see cref="Value"/>, or the empty string for <c>default</c>.</returns>
    public override string ToString() => Value ?? string.Empty;

    /// <summary>
    /// A required field was absent in the raw provider payload.
    /// </summary>
    public static readonly IssueCode MissingField = new("MISSING_FIELD");

    /// <summary>
    /// The data is older than the freshness threshold the consumer requested.
    /// </summary>
    public static readonly IssueCode StaleData = new("STALE_DATA");

    /// <summary>
    /// The provider throttled or rate-limited the request.
    /// </summary>
    public static readonly IssueCode RateLimited = new("RATE_LIMITED");

    /// <summary>
    /// A gap corresponds to a known market holiday or non-business day; the envelope
    /// is considered complete for the business-day subset.
    /// </summary>
    public static readonly IssueCode HolidayGap = new("HOLIDAY_GAP");

    /// <summary>
    /// A gap falls on a business day where data was expected but not returned —
    /// distinct from <see cref="HolidayGap"/> because the consumer should treat it as
    /// a potential data-quality problem.
    /// </summary>
    public static readonly IssueCode UnexpectedGap = new("UNEXPECTED_GAP");

    /// <summary>
    /// The provider's response shape differs from the adapter's expected schema.
    /// </summary>
    public static readonly IssueCode SchemaDrift = new("SCHEMA_DRIFT");

    /// <summary>
    /// A cached snapshot exceeds the configured staleness threshold.
    /// </summary>
    public static readonly IssueCode SnapshotStale = new("SNAPSHOT_STALE");

    /// <summary>
    /// A partition fetch failed inside a larger batched request.
    /// </summary>
    public static readonly IssueCode PartitionFailed = new("PARTITION_FAILED");
}
