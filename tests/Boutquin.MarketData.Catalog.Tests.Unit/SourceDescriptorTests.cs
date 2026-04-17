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
using Boutquin.MarketData.Abstractions.Requests;
using Boutquin.MarketData.Catalog.Descriptors;
using Boutquin.MarketData.Catalog.Governance;

using FluentAssertions;

using Xunit;

namespace Boutquin.MarketData.Catalog.Tests.Unit;

public sealed class SourceDescriptorTests
{
    [Fact]
    public void Constructor_sets_all_required_properties()
    {
        var descriptor = new SourceDescriptor(
            "nyfed-sofr",
            "NY Fed SOFR",
            DataSourceTier.Tier1OfficialPublic,
            DataLicenseFlag.PublicDomainLike,
            DataFrequency.Daily);

        descriptor.SourceId.Should().Be("nyfed-sofr");
        descriptor.DisplayName.Should().Be("NY Fed SOFR");
        descriptor.Tier.Should().Be(DataSourceTier.Tier1OfficialPublic);
        descriptor.LicenseFlag.Should().Be(DataLicenseFlag.PublicDomainLike);
        descriptor.PrimaryFrequency.Should().Be(DataFrequency.Daily);
    }

    [Fact]
    public void Optional_properties_have_sensible_defaults()
    {
        var descriptor = new SourceDescriptor(
            "test", "Test", DataSourceTier.Tier1OfficialPublic,
            DataLicenseFlag.PublicDomainLike, DataFrequency.Daily);

        descriptor.SupportsHistorical.Should().BeTrue();
        descriptor.SupportsEndOfDay.Should().BeTrue();
        descriptor.SupportsIntraday.Should().BeFalse();
        descriptor.SupportsRedistribution.Should().BeFalse();
        descriptor.Notes.Should().BeNull();
    }

    [Fact]
    public void Optional_properties_can_be_overridden()
    {
        var descriptor = new SourceDescriptor(
            "ice-swap", "ICE Swap Rate", DataSourceTier.Tier3Commercial,
            DataLicenseFlag.BenchmarkLicenseRequired, DataFrequency.Daily,
            SupportsHistorical: false,
            SupportsIntraday: true,
            SupportsRedistribution: false,
            Notes: "Requires display license");

        descriptor.SupportsHistorical.Should().BeFalse();
        descriptor.SupportsIntraday.Should().BeTrue();
        descriptor.Notes.Should().Be("Requires display license");
    }

    [Fact]
    public void Record_equality_is_value_based()
    {
        var a = new SourceDescriptor("id", "Name", DataSourceTier.Tier1OfficialPublic,
            DataLicenseFlag.PublicDomainLike, DataFrequency.Daily);
        var b = new SourceDescriptor("id", "Name", DataSourceTier.Tier1OfficialPublic,
            DataLicenseFlag.PublicDomainLike, DataFrequency.Daily);

        a.Should().Be(b);
    }

    [Fact]
    public void DataSourceTier_values_are_ordered_by_quality()
    {
        ((int)DataSourceTier.Tier1OfficialPublic).Should().BeLessThan((int)DataSourceTier.Tier2FreeIndicative);
        ((int)DataSourceTier.Tier2FreeIndicative).Should().BeLessThan((int)DataSourceTier.Tier3Commercial);
    }

    [Fact]
    public void DataLicenseFlag_has_expected_values()
    {
        Enum.GetValues<DataLicenseFlag>().Should().HaveCount(7);
    }
}
