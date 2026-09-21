using System.CodeDom.Compiler;
using System.Text;

namespace System.Resources.Tools
{
    public static class StronglyTypedResourceBuilder
    {
        public static string? VerifyResourceName(string key, CodeDomProvider provider)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            string candidate = CreateIdentifierCandidate(key);
            return provider.IsValidIdentifier(candidate) ? candidate : provider.CreateValidIdentifier(candidate);
        }

        private static string CreateIdentifierCandidate(string key)
        {
            var builder = new StringBuilder(key.Length);
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (i == 0)
                {
                    builder.Append(char.IsLetter(c) || c == '_' ? c : '_');
                    continue;
                }

                builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            }

            return builder.ToString();
        }
    }
}
