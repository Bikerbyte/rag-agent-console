using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using CPBLLineBotCloud.Models;

namespace CPBLLineBotCloud.Services;

public partial class LocalHashAdvisoryEmbeddingService(IOptions<SecurityAdvisoryOptions> options) : IAdvisoryEmbeddingService
{
    public Task<float[]> BuildEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var dimensions = Math.Clamp(options.Value.EmbeddingDimensions, 64, 2048);
        var vector = new float[dimensions];
        var tokens = TokenRegex().Matches(text.ToLowerInvariant());

        foreach (Match token in tokens)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token.Value));
            var bucket = BitConverter.ToUInt32(bytes, 0) % dimensions;
            var sign = (bytes[4] & 1) == 0 ? 1f : -1f;
            vector[bucket] += sign;
        }

        Normalize(vector);
        return Task.FromResult(vector);
    }

    private static void Normalize(float[] vector)
    {
        var sum = 0d;
        foreach (var value in vector)
        {
            sum += value * value;
        }

        var length = Math.Sqrt(sum);
        if (length <= 0)
        {
            return;
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / length);
        }
    }

    [GeneratedRegex("[a-z0-9][a-z0-9_.:-]{1,}")]
    private static partial Regex TokenRegex();
}
