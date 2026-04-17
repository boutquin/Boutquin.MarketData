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

namespace Boutquin.MarketData.ArchitectureTests.Helpers;

public abstract class BaseArchitectureTest
{
    protected static Assembly AbstractionsAssembly =>
        typeof(Abstractions.Calendars.IBusinessCalendar).Assembly;

    protected static Assembly TransportAssembly =>
        typeof(Transport.Http.TransportOptions).Assembly;

    protected static Assembly CacheAssembly =>
        typeof(Cache.Memory.ExactOnceMemoryCache).Assembly;

    protected static Assembly StorageAssembly =>
        typeof(Storage.Cdc.SnapshotChangeSource).Assembly;

    protected static Assembly CatalogAssembly =>
        typeof(Catalog.Identifiers.DatasetKey).Assembly;

    protected static Assembly DayCountAssembly =>
        typeof(DayCount.Thirty360).Assembly;

    protected static Assembly CalendarsAssembly =>
        typeof(Calendars.CompositeCalendar).Assembly;

    protected static Assembly ConventionsAssembly =>
        typeof(Conventions.InstrumentConventionRegistry).Assembly;

    protected static Assembly NormalizationAssembly =>
        typeof(Normalization.Filters.DateRangeFilter).Assembly;

    protected static Assembly OrchestrationAssembly =>
        typeof(Orchestration.Pipeline.DataPipelineOptions).Assembly;

    protected static Assembly DependencyInjectionAssembly =>
        typeof(DependencyInjection.ServiceCollectionExtensions).Assembly;

    protected static Assembly TestingAssembly =>
        typeof(Testing.Builders.EnvelopeBuilder<>).Assembly;

    protected static IEnumerable<Assembly> InfrastructureAssemblies =>
    [
        TransportAssembly,
        CacheAssembly,
        StorageAssembly,
        CatalogAssembly,
        DayCountAssembly,
        CalendarsAssembly,
    ];

    protected static IEnumerable<Assembly> AllSourceAssemblies =>
    [
        AbstractionsAssembly,
        TransportAssembly,
        CacheAssembly,
        StorageAssembly,
        CatalogAssembly,
        DayCountAssembly,
        CalendarsAssembly,
        ConventionsAssembly,
        NormalizationAssembly,
        OrchestrationAssembly,
        DependencyInjectionAssembly,
        TestingAssembly,
    ];

    protected static string GetFailingTypes(TestResult result) =>
        result.FailingTypes != null
            ? string.Join(", ", result.FailingTypes.Select(t => t.FullName))
            : string.Empty;
}
