namespace MyLife.Features.FamilyTree.Services;

/// <summary>Vietnamese lunar dates using the established new-moon / solar-longitude algorithm (UTC+7).</summary>
public sealed class VietnameseLunarCalendarService
{
    private const double TimeZone = 7;
    private const double SynodicMonth = 29.530588853;
    private readonly TimeProvider timeProvider;
    private static readonly TimeZoneInfo VietnamTimeZone = ResolveVietnamTimeZone();

    public VietnameseLunarCalendarService(TimeProvider timeProvider) => this.timeProvider = timeProvider;

    public DateOnly ConvertLunarToSolar(int day, int month, int year, bool isLeapMonth)
    {
        if (day is < 1 or > 30 || month is < 1 or > 12 || year is < 1800 or > 2200)
            throw InvalidDate();

        var a11 = month >= 11 ? LunarMonth11(year) : LunarMonth11(year - 1);
        var b11 = month >= 11 ? LunarMonth11(year + 1) : LunarMonth11(year);
        var offset = month - 11;
        if (offset < 0) offset += 12;
        var k = (int)Math.Floor(0.5 + (a11 - 2415021.076998695) / SynodicMonth);
        var leapOffset = 0;
        if (b11 - a11 > 365) leapOffset = LeapMonthOffset(a11);
        var leapMonth = leapOffset - 2;
        if (leapMonth < 0) leapMonth += 12;
        if (isLeapMonth && (leapOffset == 0 || month != leapMonth)) throw InvalidDate();
        if (leapOffset != 0 && (offset >= leapOffset || isLeapMonth)) offset++;

        var monthStart = NewMoonDay(k + offset);
        var nextMonthStart = NewMoonDay(k + offset + 1);
        if (day > nextMonthStart - monthStart) throw InvalidDate();
        return DateFromJulianDay(monthStart + day - 1);
    }

    public (DateOnly SolarDate, int DaysUntil) NextOccurrence(int day, int month, bool isLeapMonth, DateOnly? today = null)
    {
        var localToday = today ?? GetVietnamToday();
        for (var year = Math.Max(1800, localToday.Year - 1); year <= Math.Min(2200, localToday.Year + 20); year++)
        {
            DateOnly candidate;
            try { candidate = ConvertLunarToSolar(day, month, year, isLeapMonth); }
            catch (ArgumentException) { continue; } // The requested leap month/date does not occur in this lunar year.
            if (candidate >= localToday) return (candidate, candidate.DayNumber - localToday.DayNumber);
        }
        throw InvalidDate();
    }

    public bool HasPassedInCurrentSolarYear(int day, int month, bool isLeapMonth, DateOnly? today = null)
    {
        var localToday = today ?? GetVietnamToday();
        var foundCurrentYearOccurrence = false;
        var allCurrentYearOccurrencesPassed = true;
        for (var lunarYear = Math.Max(1800, localToday.Year - 1); lunarYear <= Math.Min(2200, localToday.Year + 1); lunarYear++)
        {
            DateOnly candidate;
            try { candidate = ConvertLunarToSolar(day, month, lunarYear, isLeapMonth); }
            catch (ArgumentException) { continue; }
            if (candidate.Year != localToday.Year) continue;
            foundCurrentYearOccurrence = true;
            if (candidate >= localToday) allCurrentYearOccurrencesPassed = false;
        }
        return foundCurrentYearOccurrence && allCurrentYearOccurrencesPassed;
    }

    public DateOnly GetVietnamToday()
    {
        var local = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), VietnamTimeZone);
        return DateOnly.FromDateTime(local.DateTime);
    }

    public static TimeZoneInfo ResolveVietnamTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
        return TimeZoneInfo.CreateCustomTimeZone("Vietnam Standard Time", TimeSpan.FromHours(7), "Vietnam Standard Time", "Vietnam Standard Time");
    }

    private static ArgumentException InvalidDate() => new("The lunar date does not exist in the specified Vietnamese lunar year.", "lunarDate");

    private static int JulianDay(int day, int month, int year)
    {
        var a = (14 - month) / 12;
        var y = year + 4800 - a;
        var m = month + 12 * a - 3;
        return day + (153 * m + 2) / 5 + 365 * y + y / 4 - y / 100 + y / 400 - 32045;
    }

    private static DateOnly DateFromJulianDay(int jd)
    {
        var a = jd + 32044;
        var b = (4 * a + 3) / 146097;
        var c = a - b * 146097 / 4;
        var d = (4 * c + 3) / 1461;
        var e = c - 1461 * d / 4;
        var m = (5 * e + 2) / 153;
        var day = e - (153 * m + 2) / 5 + 1;
        var month = m + 3 - 12 * (m / 10);
        var year = b * 100 + d - 4800 + m / 10;
        return new DateOnly(year, month, day);
    }

    private static int NewMoonDay(int k)
    {
        var t = k / 1236.85;
        var t2 = t * t;
        var t3 = t2 * t;
        var dr = Math.PI / 180;
        var jd1 = 2415020.75933 + 29.53058868 * k + 0.0001178 * t2 - 0.000000155 * t3
            + 0.00033 * Math.Sin((166.56 + 132.87 * t - 0.009173 * t2) * dr);
        var m = 359.2242 + 29.10535608 * k - 0.0000333 * t2 - 0.00000347 * t3;
        var mPrime = 306.0253 + 385.81691806 * k + 0.0107306 * t2 + 0.00001236 * t3;
        var f = 21.2964 + 390.67050646 * k - 0.0016528 * t2 - 0.00000239 * t3;
        var c1 = (0.1734 - 0.000393 * t) * Math.Sin(m * dr) + 0.0021 * Math.Sin(2 * m * dr)
            - 0.4068 * Math.Sin(mPrime * dr) + 0.0161 * Math.Sin(2 * mPrime * dr)
            - 0.0004 * Math.Sin(3 * mPrime * dr) + 0.0104 * Math.Sin(2 * f * dr)
            - 0.0051 * Math.Sin((m + mPrime) * dr) - 0.0074 * Math.Sin((m - mPrime) * dr)
            + 0.0004 * Math.Sin((2 * f + m) * dr) - 0.0004 * Math.Sin((2 * f - m) * dr)
            - 0.0006 * Math.Sin((2 * f + mPrime) * dr) + 0.0010 * Math.Sin((2 * f - mPrime) * dr)
            + 0.0005 * Math.Sin((2 * mPrime + m) * dr);
        var deltaT = t < -11
            ? 0.001 + 0.000839 * t + 0.0002261 * t2 - 0.00000845 * t3 - 0.000000081 * t2 * t2
            : -0.000278 + 0.000265 * t + 0.000262 * t2;
        return (int)Math.Floor(jd1 + c1 - deltaT + 0.5 + TimeZone / 24);
    }

    private static double SunLongitude(int dayNumber)
    {
        var t = (dayNumber - 2451545.5 - TimeZone / 24) / 36525;
        var t2 = t * t;
        var dr = Math.PI / 180;
        var m = 357.52910 + 35999.05030 * t - 0.0001559 * t2 - 0.00000048 * t2 * t;
        var l0 = 280.46645 + 36000.76983 * t + 0.0003032 * t2;
        var dl = (1.914600 - 0.004817 * t - 0.000014 * t2) * Math.Sin(dr * m)
            + (0.019993 - 0.000101 * t) * Math.Sin(2 * dr * m) + 0.000290 * Math.Sin(3 * dr * m);
        var l = (l0 + dl) * dr;
        l -= 2 * Math.PI * Math.Floor(l / (2 * Math.PI));
        return l;
    }

    private static int SunLongitudeSector(int dayNumber) => (int)Math.Floor(SunLongitude(dayNumber) / Math.PI * 6);

    private static int LunarMonth11(int year)
    {
        var off = JulianDay(31, 12, year) - 2415021;
        var k = (int)Math.Floor(off / SynodicMonth);
        var nm = NewMoonDay(k);
        if (SunLongitudeSector(nm) >= 9) nm = NewMoonDay(k - 1);
        return nm;
    }

    private static int LeapMonthOffset(int a11)
    {
        var k = (int)Math.Floor(0.5 + (a11 - 2415021.076998695) / SynodicMonth);
        var last = 0;
        var arc = SunLongitudeSector(NewMoonDay(k + 1));
        var i = 2;
        while (arc != last && i < 15)
        {
            last = arc;
            arc = SunLongitudeSector(NewMoonDay(k + i));
            i++;
        }
        return i - 2;
    }
}
