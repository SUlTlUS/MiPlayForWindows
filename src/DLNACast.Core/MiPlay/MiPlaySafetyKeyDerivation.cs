using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DLNACast.Core.MiPlay;

/// <summary>
/// The verified type-1 SafetyKeyDeal transformation.
/// The parameter names deliberately avoid assigning unverified source/destination endpoint semantics.
/// </summary>
public static class MiPlaySafetyKeyDerivation
{
    public const uint FirstHalfMaterialType = 1;
    public const uint SecondHalfMaterialType = 2;

    public static string DeriveType1(
        string stringA,
        ushort valueA,
        string stringB,
        ushort valueB)
    {
        ArgumentNullException.ThrowIfNull(stringA);
        ArgumentNullException.ThrowIfNull(stringB);

        var source = string.Concat(
            stringA,
            valueA.ToString(CultureInfo.InvariantCulture),
            stringB,
            valueB.ToString(CultureInfo.InvariantCulture));

        var transformed = source.ToCharArray();
        for (var index = 0; index < transformed.Length; index++)
        {
            if (transformed[index] is >= '0' and <= '9')
            {
                transformed[index] = (char)('a' + transformed[index] - '0');
            }
        }

        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(transformed))).ToLowerInvariant();
    }

    /// <summary>
    /// Selects the material used by native <c>genAesKey</c> and <c>genAesIv</c>
    /// for types 1 and 2. Type 4 is intentionally excluded because it depends on
    /// an unobserved preloaded value, not on <paramref name="authKey"/>.
    /// </summary>
    public static string SelectDerivedAesMaterial(string authKey, uint materialType)
    {
        ArgumentException.ThrowIfNullOrEmpty(authKey);

        var halfLength = authKey.Length / 2;
        return materialType switch
        {
            FirstHalfMaterialType => authKey[..halfLength],
            SecondHalfMaterialType => authKey.Substring(halfLength, halfLength),
            _ => throw new ArgumentOutOfRangeException(
                nameof(materialType),
                materialType,
                "Only SafetyKeyDeal material types 1 and 2 derive from authKey.")
        };
    }

    /// <summary>
    /// Returns the observed inbound CBC IV for the S12 SafetyInfo selection
    /// (aesKeyType=1, aesIvType=2). The native <c>genAesIv</c> type-2 branch
    /// selects the second authKey half, but the captured 192.168.10.4 challenge
    /// from local TCP port 9970 decrypts only when this inbound direction starts
    /// with the type-1 key material as its IV. This intentionally does not make
    /// an outbound or cross-device claim.
    /// </summary>
    public static string SelectObservedS12InboundSafetyIvMaterial(
        string authKey,
        uint aesKeyType,
        uint aesIvType)
    {
        if (aesKeyType != FirstHalfMaterialType || aesIvType != SecondHalfMaterialType)
        {
            throw new ArgumentOutOfRangeException(
                nameof(aesIvType),
                "Only the real-device-verified S12 inbound selection (aesKeyType=1, aesIvType=2) is supported.");
        }

        return SelectDerivedAesMaterial(authKey, FirstHalfMaterialType);
    }
}
