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

    private static readonly Regex VersionPrefixRegex = new(@"[vV](?:er)?\s*\d", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CountdownUnitRegex = new(@"\d+\s*(s|sec|secs|second|seconds|초|m|min|mins|minute|minutes|분)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // 메시지 안의 모든 정수 시퀀스 매치 (false-positive 가드용)
    private static readonly Regex DigitSequenceRegex = new(@"\d+", RegexOptions.Compiled);

    private static readonly Regex[] SystemMessageFilters = {
        new(@"^stripper\s+update\s+\d{2}\.\d{2}\.\d{2}$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^stripper\s+update\s+\d{2}/\d{2}/\d{2}$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^stripper\s+update\s+\d{4}-\d{2}-\d{2}$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^stripper\s+update", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^strippercs2", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^CONSOLE:", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\[.*\]\s*stripper", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    };

    // 60→120 으로 확장. CS2 ZE 맵에서 80s/90s/120s 카운트다운이 흔함 (boss hold, 긴 defense).
    // 너무 크게 잡으면 random 숫자를 countdown 으로 오인식할 위험 있어서 120 으로 제한.
    private const int MaxCountdownSeconds = 120;

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

    /// <summary>
    /// StripperSharp / CONSOLE: 등 플러그인 관리 메시지가 countdown 으로 오인식되는 걸 막는 필터.
    /// 번역 파이프라인은 통과시킴 (한 번 번역되면 캐시되므로 비용 영향 없고, 정보성 메시지라 HUD 노출 OK).
    /// </summary>
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

        if (VersionPrefixRegex.IsMatch(message))
            return false;

        var letters = message.Count(char.IsLetter);
        var digits = message.Count(char.IsDigit);
        var total = message.Length;

        if (letters > 0 && (double)letters / total > 0.3)
        {
            if (!CountdownUnitRegex.IsMatch(message))
                return false;

            // 추가 가드: letter-heavy + 숫자가 2개 이상이면 정보성 메시지로 간주.
            // 전수조사에서 발견된 false-positive 예:
            //   "「Duration」 5 sec 「Cooldown」 80 sec"       → 5,80 (2개) → 5초 countdown 으로 오인 방지
            //   "「Tips」 ... recover 3 HP ... 1 hp (CD:3s)"  → 3,1,3 (3개) → 3초 countdown 으로 오인 방지
            //   "Round 5 Wave 3" 같은 게임 상태 메시지도 차단
            // 진짜 countdown (예: "Run in 5 seconds", "30 seconds left") 은 숫자 1개뿐이라 통과.
            var digitSequences = DigitSequenceRegex.Matches(message).Count;
            if (digitSequences >= 2)
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
