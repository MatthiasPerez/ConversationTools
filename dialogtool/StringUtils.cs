using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace dialogtool
{
    public static class StringUtils
    {
        public static string RemoveDiacriticsAndNonAlphanumericChars(string input)
        {
            string normalized = input.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();

            foreach (char ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                {
                    if (Char.IsLetterOrDigit(ch) || ch=='#' || ch== '$' || ch == '&' || ch == '*' || ch == '+' || ch == '@')
                    {
                        builder.Append(ch);
                    }
                    else
                    {
                        builder.Append(' ');
                    }
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        public static int CalcLevenshteinDistance(string a, string b)
        {
            if (String.IsNullOrEmpty(a) || String.IsNullOrEmpty(b)) return 0;

            int lengthA = a.Length;
            int lengthB = b.Length;
            var distances = new int[lengthA + 1, lengthB + 1];
            for (int i = 0; i <= lengthA; distances[i, 0] = i++) ;
            for (int j = 0; j <= lengthB; distances[0, j] = j++) ;

            for (int i = 1; i <= lengthA; i++)
                for (int j = 1; j <= lengthB; j++)
                {
                    int cost = b[j - 1] == a[i - 1] ? 0 : 1;
                    distances[i, j] = Math.Min
                        (
                        Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                        distances[i - 1, j - 1] + cost
                        );
                }
            return distances[lengthA, lengthB];
        }


        // Return the list of the string find between two values
        // Return null if no result
        public static List<string> ExtractFromString(this string source, string start, string end)
        {
            List<String> results = null;

            string pattern = string.Format(
                "{0}({1}){2}",
                Regex.Escape(start),
                ".+?",
                 Regex.Escape(end));

            foreach (Match m in Regex.Matches(source, pattern))
            {
                if(results == null)
                {
                    results = new List<string>();
                }
                results.Add(m.Groups[1].Value);
            }

            return results;
        }

    }


}
