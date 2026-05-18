using System.Security.Cryptography;
using System.Text;

namespace OmniNode.Middleware;

public static class CoreAuthToken
{
    public static string Create()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);

        var buffer = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            buffer.Append(value.ToString("x2"));
        }

        return buffer.ToString();
    }
}
