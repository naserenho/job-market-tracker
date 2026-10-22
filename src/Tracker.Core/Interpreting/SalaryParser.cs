using System.Globalization;
using System.Text.RegularExpressions;
using Tracker.Configuration;
using Tracker.Reading;

namespace Tracker.Interpreting;

// Reads a salary range out of posting text, using the region's currency signs and context words. Postings often state
// pay next to another country's range, stipends or a bonus. For each range:
// - a foreign currency, or a foreign place label right after or before it, rules it out; so do bonus/stipend/allowance
//   amounts, and monthly amounts unless the region pays monthly (the UAE);
// - the home currency (sign or code), or a home place right after or before, wins immediately;
// - otherwise a range labelled salary/base/pay beats an unlabelled one, and only postings in the country use either.
// Only plausible yearly ranges (the region's bounds) are returned.
public sealed partial class SalaryParser
{
    private const string Amount = @"\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?";
    private const string Home = "home", Foreign = "foreign";

    private readonly RegionSalary _s;
    private readonly Regex _range, _context, _foreignContext, _nonSalaryAfter;
    private readonly string[] _homeMarks, _foreignMarks;

    public SalaryParser(RegionSalary salary)
    {
        _s = salary;
        static string CodeSign(string code) => $@"{Regex.Escape(code)}\s?\$?";
        var prefixes = salary.Signs.Select(Regex.Escape).Append(CodeSign(salary.Currency))
            .Concat(salary.ForeignSigns.Select(Regex.Escape)).Concat(salary.ForeignCurrencies.Select(CodeSign)).Append(@"\$");
        var sign = string.Join('|', prefixes);
        var codes = string.Join('|', new[] { salary.Currency }.Concat(salary.ForeignCurrencies).Select(Regex.Escape));
        var marks = string.Join('|', new[] { salary.Currency }.Concat(salary.ForeignCurrencies).Concat(salary.ShortMarks)
            .Concat(salary.ForeignShortMarks).Select(Regex.Escape));
        var units = salary.MonthlyIsSalary ? "hour|hr|year|yr|annum|month|mo" : "hour|hr|year|yr|annum";
        var adverbs = salary.MonthlyIsSalary ? "hourly|annually|per annum|monthly" : "hourly|annually|per annum";

        RangePatternText =
            $@"(?<c1>{sign}){(salary.CodeAfterAmount ? "?" : "")}\s?(?<a>{Amount})\s?(?<ka>[kK])?\s?(?<t1>{codes})?\s*(?:-|–|—|to|and)\s*(?<c2>{sign})?\s?(?<b>{Amount})\s?(?<kb>[kK])?(?<t2>\s?(?:{marks})\b)?(?<per>\s*(?:/|per|an|a)\s*(?:{units})\b|\s*(?:{adverbs})\b)?";
        ContextPatternText = RegionPatterns.Words(salary.ContextWords);
        ForeignContextPatternText = RegionPatterns.WordsOrStandaloneCodes(salary.ForeignContextWords, salary.ForeignContextCodes);
        var periods = salary.MonthlyIsSalary
            ? @"(?:per|a|an|/)\s*(?:week|wk)\b|(?:weekly|biweekly)\b"
            : @"(?:per|a|an|/)\s*(?:month|mo|week|wk)\b|(?:monthly|weekly|biweekly)\b";
        NonSalaryAfterPatternText =
            $@"^\s*(?:{codes})?\s*(?:{periods}|(?:(?!plus\b|and\b|with\b)[\w-]+\s+){{0,2}}(?:stipend|allowance|bonus|budget|reimbursement|credit)\b)";

        _range = new Regex(RangePatternText, RegexOptions.IgnoreCase);
        _context = new Regex(ContextPatternText, RegexOptions.IgnoreCase);
        _foreignContext = new Regex(ForeignContextPatternText, RegexOptions.IgnoreCase);
        _nonSalaryAfter = new Regex(NonSalaryAfterPatternText, RegexOptions.IgnoreCase);
        _homeMarks = [.. new[] { salary.Currency }.Concat(salary.Signs).Concat(salary.ShortMarks).Select(m => m.ToUpperInvariant())];
        _foreignMarks = [.. salary.ForeignCurrencies.Concat(salary.ForeignSigns).Concat(salary.ForeignShortMarks).Select(m => m.ToUpperInvariant())];
    }

    public string RangePatternText { get; }
    public string ContextPatternText { get; }
    public string ForeignContextPatternText { get; }
    public string NonSalaryAfterPatternText { get; }

    public SalaryRange? FromText(string text, bool postingInCountry)
    {
        SalaryRange? labelled = null, plain = null;
        foreach (Match m in _range.Matches(text))
        {
            var currency = Currency(m);
            if (currency == Foreign) continue;
            if (currency is null && m.Groups["c1"].Value.Length == 0) continue; // no currency at all: "Ages 25 - 40"
            var range = ToRange(m);
            if (range is null || !range.IsPlausibleWithin(_s.YearlyMin, _s.YearlyMax)) continue;

            var before = text.Substring(Math.Max(0, m.Index - 60), Math.Min(60, m.Index));
            var after = Following(text, m.Index + m.Length);
            if (NonSalaryBefore().IsMatch(before) || _nonSalaryAfter.IsMatch(after)) continue;

            if (currency == Home || _context.IsMatch(after)) return range;
            if (_foreignContext.IsMatch(after)) continue;
            if (_context.IsMatch(before)) return range;
            if (_foreignContext.IsMatch(before)) continue;
            if (SalaryLabel().IsMatch(before)) labelled ??= range;
            else plain ??= range;
        }
        return postingInCountry ? labelled ?? plain : null;
    }

    // Up to 40 characters after a range, stopping at the next amount so one range's label never reads as another's.
    private static string Following(string text, int start)
    {
        var after = text.Substring(start, Math.Min(40, text.Length - start));
        var next = after.IndexOf('$');
        return next >= 0 ? after[..next] : after;
    }

    // "foreign", "home", or null when only a plain "$" (or nothing) marks the range.
    private string? Currency(Match m)
    {
        var marks = string.Concat(m.Groups["c1"].Value, m.Groups["c2"].Value, m.Groups["t1"].Value, m.Groups["t2"].Value).ToUpperInvariant();
        if (_foreignMarks.Any(marks.Contains)) return Foreign;
        if (_homeMarks.Any(marks.Contains)) return Home;
        if (_s.BareDollar == "foreign" && marks.Contains('$')) return Foreign;
        return null;
    }

    private static SalaryRange? ToRange(Match m)
    {
        if (!TryAmount(m.Groups["a"].Value, out var a) || !TryAmount(m.Groups["b"].Value, out var b)) return null;
        var aK = m.Groups["ka"].Success;
        var bK = m.Groups["kb"].Success;
        if (aK || (bK && a < 1000)) a *= 1000;
        if (bK || (aK && b < 1000)) b *= 1000;

        var per = m.Groups["per"].Value;
        var period = HourWord().IsMatch(per) ? SalaryPeriod.Hour
            : MonthWord().IsMatch(per) ? SalaryPeriod.Month
            : a < 300 && b < 300 ? SalaryPeriod.Hour
            : SalaryPeriod.Year;
        return new SalaryRange(Math.Min(a, b), Math.Max(a, b), period);
    }

    private static bool TryAmount(string value, out decimal amount) =>
        decimal.TryParse(value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out amount);

    // "Annual bonus of $30,000 - $50,000", "Internet allowance of $100 - $150".
    [GeneratedRegex(@"\b(?:bonus|stipend|allowance|equity|commission|budget|reimbursement|per diem)\b[^.$\n]{0,30}$", RegexOptions.IgnoreCase)]
    private static partial Regex NonSalaryBefore();

    [GeneratedRegex(@"\b(?:salary|base|pay|compensation|wage|rate)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SalaryLabel();

    [GeneratedRegex(@"hour|hr", RegexOptions.IgnoreCase)]
    private static partial Regex HourWord();

    [GeneratedRegex(@"month|\bmo\b", RegexOptions.IgnoreCase)]
    private static partial Regex MonthWord();
}
