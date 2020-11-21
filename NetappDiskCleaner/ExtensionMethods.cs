using System;
using System.Collections.Generic;
using System.Text;

namespace NetappDiskCleaner
{
    public static class ExtensionMethods
    {
        public static int FindXOccurenceOfSubstring(this string fullString, string subString, int xOccurence)
        {
            int offset = fullString.IndexOf(subString);

            for (int i = 1; i < xOccurence; i++)
            {
                if (offset == -1)
                {
                    return -1;
                }

                offset = fullString.IndexOf(subString, offset + subString.Length);
            }

            return offset;
        }

        public static List<int> GetIndexesOfOccurencesOfSubstring(this string fullString, string subString)
        {
            var indexList = new List<int>();

            int indexOfSubstring;
            int lastIndex = 0;

            var tempFullString = fullString;

            while ((indexOfSubstring = tempFullString.IndexOf(subString)) != -1)
            {
                indexList.Add(indexOfSubstring + lastIndex);
                tempFullString = tempFullString.Substring(indexOfSubstring + subString.Length);

                lastIndex = lastIndex + indexOfSubstring + subString.Length;
            }

            return indexList;
        }
    }
}
