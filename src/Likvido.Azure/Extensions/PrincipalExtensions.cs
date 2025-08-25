using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Principal;

namespace Likvido.Azure.Extensions
{
    public static class PrincipalExtensions
    {
        public static List<KeyValuePair<string, string>> GetAllClaims(this IPrincipal principal)
        {
            var result = new List<KeyValuePair<string, string>>();
            if (principal is null)
            {
                return result;
            }

            if (principal is ClaimsPrincipal claimsPrincipal)
            {
                // Enumerate all claims across all identities
                foreach (var claim in claimsPrincipal.Claims)
                {
                    if (claim is null)
                    {
                        continue;
                    }

                    result.Add(new KeyValuePair<string, string>(claim.Type, claim.Value));
                }
            }

            return result;
        }
    }
}
