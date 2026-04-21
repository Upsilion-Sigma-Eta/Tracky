using System.Text;

namespace Tracky.Core.Search;

public static class IssueSearchParser
{
    public static IssueSearchQuery Parse(string? rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return IssueSearchQuery.Empty;
        }

        var textTerms = new List<string>();
        var operators = new List<IssueSearchOperator>();

        foreach (var token in Tokenize(rawQuery))
        {
            var isNegated = token[0] == '-' && token.Length > 1;
            var normalizedToken = isNegated ? token[1..] : token;
            var separatorIndex = normalizedToken.IndexOf(':', StringComparison.Ordinal);

            // GitHub 스타일 검색의 핵심은 "key:value" 토큰과 일반 텍스트 토큰을 함께 평가하는 것이다.
            // 알 수 없는 key도 파서 단계에서는 버리지 않고, 호출자가 자신이 가진 필드 기준으로 해석하게 둔다.
            if (separatorIndex > 0 && separatorIndex < normalizedToken.Length - 1)
            {
                operators.Add(
                    new IssueSearchOperator(
                        normalizedToken[..separatorIndex].Trim(),
                        normalizedToken[(separatorIndex + 1)..].Trim().Trim('"'),
                        isNegated));
                continue;
            }

            textTerms.Add(token.Trim('"'));
        }

        return new IssueSearchQuery(textTerms, operators);
    }

    public static IReadOnlyList<string> Tokenize(string rawQuery)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        var inQuotes = false;

        foreach (var character in rawQuery)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                builder.Append(character);
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                FlushToken(tokens, builder);
                continue;
            }

            builder.Append(character);
        }

        FlushToken(tokens, builder);
        return tokens;
    }

    private static void FlushToken(List<string> tokens, StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        var token = builder.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(token))
        {
            tokens.Add(token);
        }

        builder.Clear();
    }
}
