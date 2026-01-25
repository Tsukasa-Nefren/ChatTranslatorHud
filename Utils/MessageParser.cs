using System.Text.RegularExpressions;

namespace ChatTranslatorHud.Utils;

public enum MessageType { Static, Countdown }

public class ParseResult
{
    public bool IsValid { get; set; }
    public MessageType Type { get; set; }
    public int Seconds { get; set; }
    public string Prefix { get; set; } = "";
    public string Suffix { get; set; } = "";
    public bool IsMmss { get; set; }
    public string Unit { get; set; } = "";
}

public static class MessageParser
{
    private static readonly Regex[] CompiledRegexes = {
        new(@"\d+\s*[vVxX/]\s*\d+", RegexOptions.Compiled),
        new(@"(?<!\d)(\d{1,3}):([0-5]?\d)(?!\d)", RegexOptions.Compiled),
        new(@"(?<![A-Za-z0-9])(\d{1,4})\s*(s|sec|secs|second|seconds|초|m|min|mins|minute|minutes|분)\b", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(\d{1,4})\s*[.!?]?\s*$", RegexOptions.Compiled),
        new(@"\b(\d{1,4})\b", RegexOptions.Compiled)
    };
    
    private static readonly Regex BracketCountdownRegex = new(
        @"^[\s<>＜＞〈〉《》\[\]【】「」『』\(\)（）]*(\d{1,4})[\s<>＜＞〈〉《》\[\]【】「」『』\(\)（）]*$", 
        RegexOptions.Compiled);

    private static readonly Regex[] SystemMessageFilters = {
        new(@"^stripper\s+update\s+\d{2}\.\d{2}\.\d{2}$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^stripper\s+update\s+\d{2}/\d{2}/\d{2}$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^stripper\s+update\s+\d{4}-\d{2}-\d{2}$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^stripper\s+update", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^strippercs2", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^CONSOLE:", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\[.*\]\s*stripper", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    private const int MaxCountdownSeconds = 61;

    public static ParseResult TryParseMessage(string message)
    {
        var result = new ParseResult { IsValid = false };
        
        if (string.IsNullOrWhiteSpace(message))
            return result;

        if (IsSystemMessage(message))
            return result;

        try
        {
            if (CompiledRegexes[0].IsMatch(message))
                return result;
            
            var m1 = CompiledRegexes[1].Match(message);
            if (m1.Success)
            {
                var m = int.Parse(m1.Groups[1].Value);
                var s = int.Parse(m1.Groups[2].Value);
                var total = m * 60 + s;
                if (total > 0 && total <= MaxCountdownSeconds)
                {
                    result.IsValid = true;
                    result.Type = MessageType.Countdown;
                    result.Seconds = total;
                    result.Prefix = message[..m1.Index];
                    result.Suffix = message[(m1.Index + m1.Length)..];
                    result.IsMmss = true;
                    return result;
                }
            }
            
            var m2 = CompiledRegexes[2].Match(message);
            if (m2.Success)
            {
                var v = int.Parse(m2.Groups[1].Value);
                var unit = m2.Groups[2].Value ?? "";
                var total = unit.StartsWith("m", StringComparison.OrdinalIgnoreCase) || unit == "분" ? v * 60 : v;
                if (total > 0 && total <= MaxCountdownSeconds)
                {
                    result.IsValid = true;
                    result.Type = MessageType.Countdown;
                    result.Seconds = total;
                    result.Prefix = message[..m2.Index];
                    result.Suffix = message[(m2.Index + m2.Length)..];
                    result.IsMmss = false;
                    result.Unit = unit;
                    return result;
                }
            }
            
            var m3 = CompiledRegexes[3].Match(message);
            if (m3.Success)
            {
                var total = int.Parse(m3.Groups[1].Value);
                if (total > 0 && total <= MaxCountdownSeconds)
                {
                    result.IsValid = true;
                    result.Type = MessageType.Countdown;
                    result.Seconds = total;
                    result.Prefix = "";
                    result.Suffix = "";
                    result.IsMmss = false;
                    return result;
                }
            }
            
            var mBracket = BracketCountdownRegex.Match(message);
            if (mBracket.Success)
            {
                var total = int.Parse(mBracket.Groups[1].Value);
                if (total > 0 && total <= MaxCountdownSeconds)
                {
                    result.IsValid = true;
                    result.Type = MessageType.Countdown;
                    result.Seconds = total;
                    var numIndex = mBracket.Groups[1].Index;
                    var numLength = mBracket.Groups[1].Length;
                    result.Prefix = message[..numIndex];
                    result.Suffix = message[(numIndex + numLength)..];
                    result.IsMmss = false;
                    return result;
                }
            }

            var m4 = CompiledRegexes[4].Match(message);
            if (m4.Success)
            {
                var total = int.Parse(m4.Groups[1].Value);
                if (total > 0 && total <= MaxCountdownSeconds)
                {
                    result.IsValid = true;
                    result.Type = MessageType.Countdown;
                    result.Seconds = total;
                    result.Prefix = message[..m4.Index];
                    result.Suffix = message[(m4.Index + m4.Length)..];
                    result.IsMmss = false;
                    return result;
                }
            }
        }
        catch (Exception)
        {
            return result;
        }
        
        return result;
    }

    private static bool IsSystemMessage(string message)
    {
        foreach (var filter in SystemMessageFilters)
        {
            if (filter.IsMatch(message))
                return true;
        }
        return false;
    }
    
    public static bool IsCountdownOnly(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        
        if (IsSystemMessage(message))
            return false;
        
        if (Regex.IsMatch(message, @"[vV](?:er)?\s*\d", RegexOptions.IgnoreCase))
            return false;
        
        var letters = message.Count(char.IsLetter);
        var digits = message.Count(char.IsDigit);
        var total = message.Length;
        
        if (letters > 0 && (double)letters / total > 0.3)
        {
            if (!Regex.IsMatch(message, @"\d+\s*(s|sec|secs|second|seconds|초|m|min|mins|minute|minutes|분)\b", RegexOptions.IgnoreCase))
                return false;
        }
        
        if (CompiledRegexes[1].IsMatch(message))
        {
            var m = CompiledRegexes[1].Match(message);
            var mins = int.Parse(m.Groups[1].Value);
            var secs = int.Parse(m.Groups[2].Value);
            var totalSecs = mins * 60 + secs;
            if (totalSecs > 0 && totalSecs <= MaxCountdownSeconds)
                return true;
        }
        
        if (CompiledRegexes[2].IsMatch(message))
        {
            var m = CompiledRegexes[2].Match(message);
            var v = int.Parse(m.Groups[1].Value);
            var unit = m.Groups[2].Value ?? "";
            var totalSecs = unit.StartsWith("m", StringComparison.OrdinalIgnoreCase) || unit == "분" ? v * 60 : v;
            if (totalSecs > 0 && totalSecs <= MaxCountdownSeconds)
                return true;
        }
        
        if (CompiledRegexes[3].IsMatch(message))
        {
            var m = CompiledRegexes[3].Match(message);
            var totalSecs = int.Parse(m.Groups[1].Value);
            if (totalSecs > 0 && totalSecs <= MaxCountdownSeconds)
                return true;
        }
        
        if (BracketCountdownRegex.IsMatch(message))
        {
            var m = BracketCountdownRegex.Match(message);
            var totalSecs = int.Parse(m.Groups[1].Value);
            if (totalSecs > 0 && totalSecs <= MaxCountdownSeconds)
                return true;
        }
        
        var nonDigitNonSymbol = message.Count(c => char.IsLetter(c));
        if (nonDigitNonSymbol <= 2)
        {
            if (CompiledRegexes[4].IsMatch(message))
            {
                var m = CompiledRegexes[4].Match(message);
                var totalSecs = int.Parse(m.Groups[1].Value);
                if (totalSecs > 0 && totalSecs <= MaxCountdownSeconds)
                    return true;
            }
        }
        
        return false;
    }
}
