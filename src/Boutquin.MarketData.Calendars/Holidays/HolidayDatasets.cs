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

namespace Boutquin.MarketData.Calendars.Holidays;

/// <summary>
/// Pre-computed holiday date sets for major financial centers (2020-2030).
/// </summary>
/// <remarks>
/// Holiday dates follow the observed-date convention: when a holiday falls on a weekend,
/// the market-standard observation day is used (typically the nearest weekday).
/// Sources: SIFMA, Bank of England, ECB TARGET2, TSX.
/// </remarks>
public static class HolidayDatasets
{
    /// <summary>
    /// Returns US SIFMA bond market holidays for 2020-2030.
    /// Covers: New Year's Day, MLK Day, Presidents' Day, Good Friday, Memorial Day,
    /// Juneteenth (from 2021), Independence Day, Labor Day, Columbus Day, Veterans Day,
    /// Thanksgiving Day, Christmas Day.
    /// </summary>
    /// <remarks>
    /// This is the bond market calendar. For the NYSE equity calendar (which does not include
    /// Columbus Day or Veterans Day), use <see cref="UsNyseCalendar"/>.
    /// </remarks>
    /// <returns>An array of observed holiday dates.</returns>
    public static DateOnly[] UsSifma() =>
    [
        // --- 2020 ---
        new(2020, 1, 1),   // New Year's Day
        new(2020, 1, 20),  // MLK Day
        new(2020, 2, 17),  // Presidents' Day
        new(2020, 4, 10),  // Good Friday
        new(2020, 5, 25),  // Memorial Day
        new(2020, 7, 3),   // Independence Day (observed, Jul 4 is Saturday)
        new(2020, 9, 7),   // Labor Day
        new(2020, 10, 12), // Columbus Day
        new(2020, 11, 11), // Veterans Day
        new(2020, 11, 26), // Thanksgiving
        new(2020, 12, 25), // Christmas

        // --- 2021 ---
        new(2021, 1, 1),   // New Year's Day
        new(2021, 1, 18),  // MLK Day
        new(2021, 2, 15),  // Presidents' Day
        new(2021, 4, 2),   // Good Friday
        new(2021, 5, 31),  // Memorial Day
        new(2021, 6, 18),  // Juneteenth (observed, Jun 19 is Saturday)
        new(2021, 7, 5),   // Independence Day (observed, Jul 4 is Sunday)
        new(2021, 9, 6),   // Labor Day
        new(2021, 10, 11), // Columbus Day
        new(2021, 11, 11), // Veterans Day
        new(2021, 11, 25), // Thanksgiving
        new(2021, 12, 24), // Christmas (observed, Dec 25 is Saturday)

        // --- 2022 ---
        new(2022, 1, 17),  // MLK Day
        new(2022, 2, 21),  // Presidents' Day
        new(2022, 4, 15),  // Good Friday
        new(2022, 5, 30),  // Memorial Day
        new(2022, 6, 20),  // Juneteenth (observed, Jun 19 is Sunday)
        new(2022, 7, 4),   // Independence Day
        new(2022, 9, 5),   // Labor Day
        new(2022, 10, 10), // Columbus Day
        new(2022, 11, 11), // Veterans Day
        new(2022, 11, 24), // Thanksgiving
        new(2022, 12, 26), // Christmas (observed, Dec 25 is Sunday)

        // --- 2023 ---
        new(2023, 1, 2),   // New Year's Day (observed, Jan 1 is Sunday)
        new(2023, 1, 16),  // MLK Day
        new(2023, 2, 20),  // Presidents' Day
        new(2023, 4, 7),   // Good Friday
        new(2023, 5, 29),  // Memorial Day
        new(2023, 6, 19),  // Juneteenth
        new(2023, 7, 4),   // Independence Day
        new(2023, 9, 4),   // Labor Day
        new(2023, 10, 9),  // Columbus Day
        new(2023, 11, 10), // Veterans Day (observed, Nov 11 is Saturday)
        new(2023, 11, 23), // Thanksgiving
        new(2023, 12, 25), // Christmas

        // --- 2024 ---
        new(2024, 1, 1),   // New Year's Day
        new(2024, 1, 15),  // MLK Day
        new(2024, 2, 19),  // Presidents' Day
        new(2024, 3, 29),  // Good Friday
        new(2024, 5, 27),  // Memorial Day
        new(2024, 6, 19),  // Juneteenth
        new(2024, 7, 4),   // Independence Day
        new(2024, 9, 2),   // Labor Day
        new(2024, 10, 14), // Columbus Day
        new(2024, 11, 11), // Veterans Day
        new(2024, 11, 28), // Thanksgiving
        new(2024, 12, 25), // Christmas

        // --- 2025 ---
        new(2025, 1, 1),   // New Year's Day
        new(2025, 1, 20),  // MLK Day
        new(2025, 2, 17),  // Presidents' Day
        new(2025, 4, 18),  // Good Friday
        new(2025, 5, 26),  // Memorial Day
        new(2025, 6, 19),  // Juneteenth
        new(2025, 7, 4),   // Independence Day
        new(2025, 9, 1),   // Labor Day
        new(2025, 10, 13), // Columbus Day
        new(2025, 11, 11), // Veterans Day
        new(2025, 11, 27), // Thanksgiving
        new(2025, 12, 25), // Christmas

        // --- 2026 ---
        new(2026, 1, 1),   // New Year's Day
        new(2026, 1, 19),  // MLK Day
        new(2026, 2, 16),  // Presidents' Day
        new(2026, 4, 3),   // Good Friday
        new(2026, 5, 25),  // Memorial Day
        new(2026, 6, 19),  // Juneteenth
        new(2026, 7, 3),   // Independence Day (observed, Jul 4 is Saturday)
        new(2026, 9, 7),   // Labor Day
        new(2026, 10, 12), // Columbus Day
        new(2026, 11, 11), // Veterans Day
        new(2026, 11, 26), // Thanksgiving
        new(2026, 12, 25), // Christmas

        // --- 2027 ---
        new(2027, 1, 1),   // New Year's Day
        new(2027, 1, 18),  // MLK Day
        new(2027, 2, 15),  // Presidents' Day
        new(2027, 3, 26),  // Good Friday
        new(2027, 5, 31),  // Memorial Day
        new(2027, 6, 18),  // Juneteenth (observed, Jun 19 is Saturday)
        new(2027, 7, 5),   // Independence Day (observed, Jul 4 is Sunday)
        new(2027, 9, 6),   // Labor Day
        new(2027, 10, 11), // Columbus Day
        new(2027, 11, 11), // Veterans Day
        new(2027, 11, 25), // Thanksgiving
        new(2027, 12, 24), // Christmas (observed, Dec 25 is Saturday)

        // --- 2028 ---
        new(2028, 1, 17),  // MLK Day
        new(2028, 2, 21),  // Presidents' Day
        new(2028, 4, 14),  // Good Friday
        new(2028, 5, 29),  // Memorial Day
        new(2028, 6, 19),  // Juneteenth
        new(2028, 7, 4),   // Independence Day
        new(2028, 9, 4),   // Labor Day
        new(2028, 10, 9),  // Columbus Day
        new(2028, 11, 10), // Veterans Day (observed, Nov 11 is Saturday)
        new(2028, 11, 23), // Thanksgiving
        new(2028, 12, 25), // Christmas

        // --- 2029 ---
        new(2029, 1, 1),   // New Year's Day
        new(2029, 1, 15),  // MLK Day
        new(2029, 2, 19),  // Presidents' Day
        new(2029, 3, 30),  // Good Friday
        new(2029, 5, 28),  // Memorial Day
        new(2029, 6, 19),  // Juneteenth
        new(2029, 7, 4),   // Independence Day
        new(2029, 9, 3),   // Labor Day
        new(2029, 10, 8),  // Columbus Day
        new(2029, 11, 12), // Veterans Day (observed, Nov 11 is Sunday)
        new(2029, 11, 22), // Thanksgiving
        new(2029, 12, 25), // Christmas

        // --- 2030 ---
        new(2030, 1, 1),   // New Year's Day
        new(2030, 1, 21),  // MLK Day
        new(2030, 2, 18),  // Presidents' Day
        new(2030, 4, 19),  // Good Friday
        new(2030, 5, 27),  // Memorial Day
        new(2030, 6, 19),  // Juneteenth
        new(2030, 7, 4),   // Independence Day
        new(2030, 9, 2),   // Labor Day
        new(2030, 10, 14), // Columbus Day
        new(2030, 11, 11), // Veterans Day
        new(2030, 11, 28), // Thanksgiving
        new(2030, 12, 25), // Christmas
    ];

    /// <summary>
    /// Returns Bank of England holidays for 2020-2030.
    /// Covers: New Year's Day, Good Friday, Easter Monday, Early May bank holiday,
    /// Spring bank holiday, Summer bank holiday, Christmas Day, Boxing Day.
    /// </summary>
    /// <returns>An array of observed holiday dates.</returns>
    public static DateOnly[] GbLondon() =>
    [
        // --- 2020 ---
        new(2020, 1, 1),   // New Year's Day
        new(2020, 4, 10),  // Good Friday
        new(2020, 4, 13),  // Easter Monday
        new(2020, 5, 8),   // Early May bank holiday (moved for VE Day 75th)
        new(2020, 5, 25),  // Spring bank holiday
        new(2020, 8, 31),  // Summer bank holiday
        new(2020, 12, 25), // Christmas Day
        new(2020, 12, 28), // Boxing Day (observed, Dec 26 is Saturday)

        // --- 2021 ---
        new(2021, 1, 1),   // New Year's Day
        new(2021, 4, 2),   // Good Friday
        new(2021, 4, 5),   // Easter Monday
        new(2021, 5, 3),   // Early May bank holiday
        new(2021, 5, 31),  // Spring bank holiday
        new(2021, 8, 30),  // Summer bank holiday
        new(2021, 12, 27), // Christmas Day (observed, Dec 25 is Saturday)
        new(2021, 12, 28), // Boxing Day (observed, Dec 26 is Sunday)

        // --- 2022 ---
        new(2022, 1, 3),   // New Year's Day (observed, Jan 1 is Saturday)
        new(2022, 4, 15),  // Good Friday
        new(2022, 4, 18),  // Easter Monday
        new(2022, 5, 2),   // Early May bank holiday
        new(2022, 6, 2),   // Spring bank holiday (moved for Queen's Platinum Jubilee)
        new(2022, 6, 3),   // Platinum Jubilee bank holiday
        new(2022, 8, 29),  // Summer bank holiday
        new(2022, 9, 19),  // State Funeral of Queen Elizabeth II
        new(2022, 12, 26), // Boxing Day
        new(2022, 12, 27), // Christmas Day (observed, Dec 25 is Sunday)

        // --- 2023 ---
        new(2023, 1, 2),   // New Year's Day (observed, Jan 1 is Sunday)
        new(2023, 4, 7),   // Good Friday
        new(2023, 4, 10),  // Easter Monday
        new(2023, 5, 1),   // Early May bank holiday
        new(2023, 5, 8),   // Coronation bank holiday
        new(2023, 5, 29),  // Spring bank holiday
        new(2023, 8, 28),  // Summer bank holiday
        new(2023, 12, 25), // Christmas Day
        new(2023, 12, 26), // Boxing Day

        // --- 2024 ---
        new(2024, 1, 1),   // New Year's Day
        new(2024, 3, 29),  // Good Friday
        new(2024, 4, 1),   // Easter Monday
        new(2024, 5, 6),   // Early May bank holiday
        new(2024, 5, 27),  // Spring bank holiday
        new(2024, 8, 26),  // Summer bank holiday
        new(2024, 12, 25), // Christmas Day
        new(2024, 12, 26), // Boxing Day

        // --- 2025 ---
        new(2025, 1, 1),   // New Year's Day
        new(2025, 4, 18),  // Good Friday
        new(2025, 4, 21),  // Easter Monday
        new(2025, 5, 5),   // Early May bank holiday
        new(2025, 5, 26),  // Spring bank holiday
        new(2025, 8, 25),  // Summer bank holiday
        new(2025, 12, 25), // Christmas Day
        new(2025, 12, 26), // Boxing Day

        // --- 2026 ---
        new(2026, 1, 1),   // New Year's Day
        new(2026, 4, 3),   // Good Friday
        new(2026, 4, 6),   // Easter Monday
        new(2026, 5, 4),   // Early May bank holiday
        new(2026, 5, 25),  // Spring bank holiday
        new(2026, 8, 31),  // Summer bank holiday
        new(2026, 12, 25), // Christmas Day
        new(2026, 12, 28), // Boxing Day (observed, Dec 26 is Saturday)

        // --- 2027 ---
        new(2027, 1, 1),   // New Year's Day
        new(2027, 3, 26),  // Good Friday
        new(2027, 3, 29),  // Easter Monday
        new(2027, 5, 3),   // Early May bank holiday
        new(2027, 5, 31),  // Spring bank holiday
        new(2027, 8, 30),  // Summer bank holiday
        new(2027, 12, 27), // Christmas Day (observed, Dec 25 is Saturday)
        new(2027, 12, 28), // Boxing Day (observed, Dec 26 is Sunday)

        // --- 2028 ---
        new(2028, 1, 3),   // New Year's Day (observed, Jan 1 is Saturday)
        new(2028, 4, 14),  // Good Friday
        new(2028, 4, 17),  // Easter Monday
        new(2028, 5, 1),   // Early May bank holiday
        new(2028, 5, 29),  // Spring bank holiday
        new(2028, 8, 28),  // Summer bank holiday
        new(2028, 12, 25), // Christmas Day
        new(2028, 12, 26), // Boxing Day

        // --- 2029 ---
        new(2029, 1, 1),   // New Year's Day
        new(2029, 3, 30),  // Good Friday
        new(2029, 4, 2),   // Easter Monday
        new(2029, 5, 7),   // Early May bank holiday
        new(2029, 5, 28),  // Spring bank holiday
        new(2029, 8, 27),  // Summer bank holiday
        new(2029, 12, 25), // Christmas Day
        new(2029, 12, 26), // Boxing Day

        // --- 2030 ---
        new(2030, 1, 1),   // New Year's Day
        new(2030, 4, 19),  // Good Friday
        new(2030, 4, 22),  // Easter Monday
        new(2030, 5, 6),   // Early May bank holiday
        new(2030, 5, 27),  // Spring bank holiday
        new(2030, 8, 26),  // Summer bank holiday
        new(2030, 12, 25), // Christmas Day
        new(2030, 12, 26), // Boxing Day
    ];

    /// <summary>
    /// Returns ECB TARGET2 holidays for 2020-2030.
    /// Covers: New Year's Day, Good Friday, Easter Monday, Labour Day (May 1),
    /// Christmas Day, 26 December.
    /// </summary>
    /// <returns>An array of observed holiday dates.</returns>
    public static DateOnly[] Target() =>
    [
        // --- 2020 ---
        new(2020, 1, 1),   // New Year's Day
        new(2020, 4, 10),  // Good Friday
        new(2020, 4, 13),  // Easter Monday
        new(2020, 5, 1),   // Labour Day
        new(2020, 12, 25), // Christmas Day
        new(2020, 12, 28), // 26 December (observed, Dec 26 is Saturday)

        // --- 2021 ---
        new(2021, 1, 1),   // New Year's Day
        new(2021, 4, 2),   // Good Friday
        new(2021, 4, 5),   // Easter Monday
        new(2021, 12, 27), // Christmas Day (observed, Dec 25 is Saturday)
        new(2021, 12, 28), // 26 December (observed, Dec 26 is Sunday)

        // --- 2022 ---
        new(2022, 4, 15),  // Good Friday
        new(2022, 4, 18),  // Easter Monday
        new(2022, 12, 26), // 26 December
        new(2022, 12, 27), // Christmas Day (observed, Dec 25 is Sunday)

        // --- 2023 ---
        new(2023, 1, 2),   // New Year's Day (observed, Jan 1 is Sunday)
        new(2023, 4, 7),   // Good Friday
        new(2023, 4, 10),  // Easter Monday
        new(2023, 5, 1),   // Labour Day
        new(2023, 12, 25), // Christmas Day
        new(2023, 12, 26), // 26 December

        // --- 2024 ---
        new(2024, 1, 1),   // New Year's Day
        new(2024, 3, 29),  // Good Friday
        new(2024, 4, 1),   // Easter Monday
        new(2024, 5, 1),   // Labour Day
        new(2024, 12, 25), // Christmas Day
        new(2024, 12, 26), // 26 December

        // --- 2025 ---
        new(2025, 1, 1),   // New Year's Day
        new(2025, 4, 18),  // Good Friday
        new(2025, 4, 21),  // Easter Monday
        new(2025, 5, 1),   // Labour Day
        new(2025, 12, 25), // Christmas Day
        new(2025, 12, 26), // 26 December

        // --- 2026 ---
        new(2026, 1, 1),   // New Year's Day
        new(2026, 4, 3),   // Good Friday
        new(2026, 4, 6),   // Easter Monday
        new(2026, 5, 1),   // Labour Day
        new(2026, 12, 25), // Christmas Day
        new(2026, 12, 28), // 26 December (observed, Dec 26 is Saturday)

        // --- 2027 ---
        new(2027, 1, 1),   // New Year's Day
        new(2027, 3, 26),  // Good Friday
        new(2027, 3, 29),  // Easter Monday
        new(2027, 12, 27), // Christmas Day (observed, Dec 25 is Saturday)
        new(2027, 12, 28), // 26 December (observed, Dec 26 is Sunday)

        // --- 2028 ---
        new(2028, 4, 14),  // Good Friday
        new(2028, 4, 17),  // Easter Monday
        new(2028, 5, 1),   // Labour Day
        new(2028, 12, 25), // Christmas Day
        new(2028, 12, 26), // 26 December

        // --- 2029 ---
        new(2029, 1, 1),   // New Year's Day
        new(2029, 3, 30),  // Good Friday
        new(2029, 4, 2),   // Easter Monday
        new(2029, 5, 1),   // Labour Day
        new(2029, 12, 25), // Christmas Day
        new(2029, 12, 26), // 26 December

        // --- 2030 ---
        new(2030, 1, 1),   // New Year's Day
        new(2030, 4, 19),  // Good Friday
        new(2030, 4, 22),  // Easter Monday
        new(2030, 5, 1),   // Labour Day
        new(2030, 12, 25), // Christmas Day
        new(2030, 12, 26), // 26 December
    ];

    /// <summary>
    /// Returns Canadian statutory holidays for 2020-2030.
    /// Covers: New Year's Day, Family Day, Good Friday, Victoria Day, Canada Day,
    /// Civic Holiday, Labour Day, Truth and Reconciliation Day (from 2021),
    /// Thanksgiving Day, Remembrance Day, Christmas Day, Boxing Day.
    /// </summary>
    /// <remarks>
    /// This is the statutory holiday calendar. The TSX does not close for Truth and
    /// Reconciliation Day or Remembrance Day. For the TSX equity calendar, use
    /// <see cref="CaTsxCalendar"/>.
    /// </remarks>
    /// <returns>An array of observed holiday dates.</returns>
    public static DateOnly[] CaStatutory() =>
    [
        // --- 2020 ---
        new(2020, 1, 1),   // New Year's Day
        new(2020, 2, 17),  // Family Day
        new(2020, 4, 10),  // Good Friday
        new(2020, 5, 18),  // Victoria Day
        new(2020, 7, 1),   // Canada Day
        new(2020, 8, 3),   // Civic Holiday
        new(2020, 9, 7),   // Labour Day
        new(2020, 9, 30),  // Truth and Reconciliation Day
        new(2020, 10, 12), // Thanksgiving
        new(2020, 11, 11), // Remembrance Day
        new(2020, 12, 25), // Christmas Day
        new(2020, 12, 28), // Boxing Day (observed, Dec 26 is Saturday)

        // --- 2021 ---
        new(2021, 1, 1),   // New Year's Day
        new(2021, 2, 15),  // Family Day
        new(2021, 4, 2),   // Good Friday
        new(2021, 5, 24),  // Victoria Day
        new(2021, 7, 1),   // Canada Day
        new(2021, 8, 2),   // Civic Holiday
        new(2021, 9, 6),   // Labour Day
        new(2021, 9, 30),  // Truth and Reconciliation Day
        new(2021, 10, 11), // Thanksgiving
        new(2021, 11, 11), // Remembrance Day
        new(2021, 12, 27), // Christmas Day (observed, Dec 25 is Saturday)
        new(2021, 12, 28), // Boxing Day (observed, Dec 26 is Sunday)

        // --- 2022 ---
        new(2022, 1, 3),   // New Year's Day (observed, Jan 1 is Saturday)
        new(2022, 2, 21),  // Family Day
        new(2022, 4, 15),  // Good Friday
        new(2022, 5, 23),  // Victoria Day
        new(2022, 7, 1),   // Canada Day
        new(2022, 8, 1),   // Civic Holiday
        new(2022, 9, 5),   // Labour Day
        new(2022, 9, 30),  // Truth and Reconciliation Day
        new(2022, 10, 10), // Thanksgiving
        new(2022, 11, 11), // Remembrance Day
        new(2022, 12, 26), // Boxing Day
        new(2022, 12, 27), // Christmas Day (observed, Dec 25 is Sunday)

        // --- 2023 ---
        new(2023, 1, 2),   // New Year's Day (observed, Jan 1 is Sunday)
        new(2023, 2, 20),  // Family Day
        new(2023, 4, 7),   // Good Friday
        new(2023, 5, 22),  // Victoria Day
        new(2023, 7, 3),   // Canada Day (observed, Jul 1 is Saturday)
        new(2023, 8, 7),   // Civic Holiday
        new(2023, 9, 4),   // Labour Day
        new(2023, 10, 2),  // Truth and Reconciliation Day (observed, Sep 30 is Saturday)
        new(2023, 10, 9),  // Thanksgiving
        new(2023, 11, 13), // Remembrance Day (observed, Nov 11 is Saturday)
        new(2023, 12, 25), // Christmas Day
        new(2023, 12, 26), // Boxing Day

        // --- 2024 ---
        new(2024, 1, 1),   // New Year's Day
        new(2024, 2, 19),  // Family Day
        new(2024, 3, 29),  // Good Friday
        new(2024, 5, 20),  // Victoria Day
        new(2024, 7, 1),   // Canada Day
        new(2024, 8, 5),   // Civic Holiday
        new(2024, 9, 2),   // Labour Day
        new(2024, 9, 30),  // Truth and Reconciliation Day
        new(2024, 10, 14), // Thanksgiving
        new(2024, 11, 11), // Remembrance Day
        new(2024, 12, 25), // Christmas Day
        new(2024, 12, 26), // Boxing Day

        // --- 2025 ---
        new(2025, 1, 1),   // New Year's Day
        new(2025, 2, 17),  // Family Day
        new(2025, 4, 18),  // Good Friday
        new(2025, 5, 19),  // Victoria Day
        new(2025, 7, 1),   // Canada Day
        new(2025, 8, 4),   // Civic Holiday
        new(2025, 9, 1),   // Labour Day
        new(2025, 9, 30),  // Truth and Reconciliation Day
        new(2025, 10, 13), // Thanksgiving
        new(2025, 11, 11), // Remembrance Day
        new(2025, 12, 25), // Christmas Day
        new(2025, 12, 26), // Boxing Day

        // --- 2026 ---
        new(2026, 1, 1),   // New Year's Day
        new(2026, 2, 16),  // Family Day
        new(2026, 4, 3),   // Good Friday
        new(2026, 5, 18),  // Victoria Day
        new(2026, 7, 1),   // Canada Day
        new(2026, 8, 3),   // Civic Holiday
        new(2026, 9, 7),   // Labour Day
        new(2026, 9, 30),  // Truth and Reconciliation Day
        new(2026, 10, 12), // Thanksgiving
        new(2026, 11, 11), // Remembrance Day
        new(2026, 12, 25), // Christmas Day
        new(2026, 12, 28), // Boxing Day (observed, Dec 26 is Saturday)

        // --- 2027 ---
        new(2027, 1, 1),   // New Year's Day
        new(2027, 2, 15),  // Family Day
        new(2027, 3, 26),  // Good Friday
        new(2027, 5, 24),  // Victoria Day
        new(2027, 7, 1),   // Canada Day
        new(2027, 8, 2),   // Civic Holiday
        new(2027, 9, 6),   // Labour Day
        new(2027, 9, 30),  // Truth and Reconciliation Day
        new(2027, 10, 11), // Thanksgiving
        new(2027, 11, 11), // Remembrance Day
        new(2027, 12, 27), // Christmas Day (observed, Dec 25 is Saturday)
        new(2027, 12, 28), // Boxing Day (observed, Dec 26 is Sunday)

        // --- 2028 ---
        new(2028, 1, 3),   // New Year's Day (observed, Jan 1 is Saturday)
        new(2028, 2, 21),  // Family Day
        new(2028, 4, 14),  // Good Friday
        new(2028, 5, 22),  // Victoria Day
        new(2028, 7, 3),   // Canada Day (observed, Jul 1 is Saturday)
        new(2028, 8, 7),   // Civic Holiday
        new(2028, 9, 4),   // Labour Day
        new(2028, 10, 2),  // Truth and Reconciliation Day (observed, Sep 30 is Saturday)
        new(2028, 10, 9),  // Thanksgiving
        new(2028, 11, 13), // Remembrance Day (observed, Nov 11 is Saturday)
        new(2028, 12, 25), // Christmas Day
        new(2028, 12, 26), // Boxing Day

        // --- 2029 ---
        new(2029, 1, 1),   // New Year's Day
        new(2029, 2, 19),  // Family Day
        new(2029, 3, 30),  // Good Friday
        new(2029, 5, 21),  // Victoria Day
        new(2029, 7, 2),   // Canada Day (observed, Jul 1 is Sunday)
        new(2029, 8, 6),   // Civic Holiday
        new(2029, 9, 3),   // Labour Day
        new(2029, 10, 1),  // Truth and Reconciliation Day (observed, Sep 30 is Sunday)
        new(2029, 10, 8),  // Thanksgiving
        new(2029, 11, 12), // Remembrance Day (observed, Nov 11 is Sunday)
        new(2029, 12, 25), // Christmas Day
        new(2029, 12, 26), // Boxing Day

        // --- 2030 ---
        new(2030, 1, 1),   // New Year's Day
        new(2030, 2, 18),  // Family Day
        new(2030, 4, 19),  // Good Friday
        new(2030, 5, 20),  // Victoria Day
        new(2030, 7, 1),   // Canada Day
        new(2030, 8, 5),   // Civic Holiday
        new(2030, 9, 2),   // Labour Day
        new(2030, 9, 30),  // Truth and Reconciliation Day
        new(2030, 10, 14), // Thanksgiving
        new(2030, 11, 11), // Remembrance Day
        new(2030, 12, 25), // Christmas Day
        new(2030, 12, 26), // Boxing Day
    ];

    /// <summary>
    /// Returns CME Group full-closure holidays for 2020-2030.
    /// Covers only days when no settlement prices are published: New Year's Day,
    /// Good Friday, Christmas Day.
    /// </summary>
    /// <remarks>
    /// CME remains open (with early halts) on MLK Day, Presidents' Day, Memorial Day,
    /// Juneteenth, Independence Day, Labor Day, and Thanksgiving — settlement prices
    /// are still published on those days. Columbus Day and Veterans Day are normal
    /// trading days for CME (unlike SIFMA/NYSE).
    /// </remarks>
    /// <returns>An array of observed holiday dates.</returns>
    public static DateOnly[] CmeGroup() =>
    [
        // --- 2020 ---
        new(2020, 1, 1),   // New Year's Day
        new(2020, 4, 10),  // Good Friday
        new(2020, 12, 25), // Christmas Day

        // --- 2021 ---
        new(2021, 1, 1),   // New Year's Day
        new(2021, 4, 2),   // Good Friday
        new(2021, 12, 24), // Christmas Day (observed, Dec 25 is Saturday)

        // --- 2022 ---
        new(2022, 4, 15),  // Good Friday
        new(2022, 12, 26), // Christmas Day (observed, Dec 25 is Sunday)

        // --- 2023 ---
        new(2023, 1, 2),   // New Year's Day (observed, Jan 1 is Sunday)
        new(2023, 4, 7),   // Good Friday
        new(2023, 12, 25), // Christmas Day

        // --- 2024 ---
        new(2024, 1, 1),   // New Year's Day
        new(2024, 3, 29),  // Good Friday
        new(2024, 12, 25), // Christmas Day

        // --- 2025 ---
        new(2025, 1, 1),   // New Year's Day
        new(2025, 4, 18),  // Good Friday
        new(2025, 12, 25), // Christmas Day

        // --- 2026 ---
        new(2026, 1, 1),   // New Year's Day
        new(2026, 4, 3),   // Good Friday
        new(2026, 12, 25), // Christmas Day

        // --- 2027 ---
        new(2027, 1, 1),   // New Year's Day
        new(2027, 3, 26),  // Good Friday
        new(2027, 12, 24), // Christmas Day (observed, Dec 25 is Saturday)

        // --- 2028 ---
        new(2028, 4, 14),  // Good Friday
        new(2028, 12, 25), // Christmas Day

        // --- 2029 ---
        new(2029, 1, 1),   // New Year's Day
        new(2029, 3, 30),  // Good Friday
        new(2029, 12, 25), // Christmas Day

        // --- 2030 ---
        new(2030, 1, 1),   // New Year's Day
        new(2030, 4, 19),  // Good Friday
        new(2030, 12, 25), // Christmas Day
    ];
}
