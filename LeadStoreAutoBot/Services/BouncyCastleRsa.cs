using System.Security.Cryptography;
using BcMath = Org.BouncyCastle.Math;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Parameters;

namespace LeadStoreAutoBot.Services;

/// <summary>
/// Обёртка над BouncyCastle RSA, реализующая контракт System.Security.Cryptography.RSA.
/// Используется когда системный Windows-криптопровайдер сломан (CNG/CryptoAPI падают
/// с 0xc1000001 / 0x8007001F).
///
/// Подпись делается на чистой математике (BigInteger.ModPow) — обходит и Windows API,
/// и Bellcore-атака чек встроенный в BC SignerUtilities (который иногда ложно срабатывает
/// "RSA engine faulty decryption/signing detected").
/// </summary>
public class BouncyCastleRsa : RSA
{
    private readonly RsaPrivateCrtKeyParameters _privateKey;
    private readonly RSAParameters _parameters;

    public BouncyCastleRsa(RsaPrivateCrtKeyParameters key)
    {
        _privateKey = key;
        _parameters = ToRsaParameters(key);
        KeySizeValue = key.Modulus.BitLength;
    }

    public override RSAParameters ExportParameters(bool includePrivateParameters)
    {
        if (!includePrivateParameters)
        {
            return new RSAParameters
            {
                Modulus  = _parameters.Modulus,
                Exponent = _parameters.Exponent,
            };
        }
        return _parameters;
    }

    public override void ImportParameters(RSAParameters parameters)
        => throw new NotSupportedException("BouncyCastleRsa создаётся через конструктор.");

    /// <summary>Главная точка вызова из Google.Apis.Auth для подписи JWT.</summary>
    public override byte[] SignData(byte[] data, int offset, int count, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        if (padding != RSASignaturePadding.Pkcs1)
            throw new NotSupportedException($"Только Pkcs1 padding поддерживается, получено: {padding}");

        // 1. Считаем хэш через BC (не трогаем Windows crypto)
        var digest = CreateDigest(hashAlgorithm.Name);
        digest.BlockUpdate(data, offset, count);
        var hash = new byte[digest.GetDigestSize()];
        digest.DoFinal(hash, 0);

        return SignHashInternal(hash, hashAlgorithm);
    }

    public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        if (padding != RSASignaturePadding.Pkcs1)
            throw new NotSupportedException();
        return SignHashInternal(hash, hashAlgorithm);
    }

    public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        if (padding != RSASignaturePadding.Pkcs1) return false;

        var em = BuildPkcs1V15(hash, hashAlgorithm);

        var s = new BcMath.BigInteger(1, signature);
        var m = s.ModPow(_privateKey.PublicExponent, _privateKey.Modulus);
        var recovered = m.ToByteArrayUnsigned();

        // Левый pad нулями до длины em
        if (recovered.Length < em.Length)
        {
            var padded = new byte[em.Length];
            Buffer.BlockCopy(recovered, 0, padded, em.Length - recovered.Length, recovered.Length);
            recovered = padded;
        }

        if (recovered.Length != em.Length) return false;
        for (int i = 0; i < em.Length; i++)
            if (recovered[i] != em[i]) return false;
        return true;
    }

    /// <summary>RSA signing руками: m = OS2IP(EM); s = m^d mod n; sig = I2OSP(s, k).</summary>
    private byte[] SignHashInternal(byte[] hash, HashAlgorithmName hashAlgorithm)
    {
        var em = BuildPkcs1V15(hash, hashAlgorithm);

        var m = new BcMath.BigInteger(1, em);
        var s = m.ModPow(_privateKey.Exponent, _privateKey.Modulus);
        var sig = s.ToByteArrayUnsigned();

        // I2OSP: длина = k байт
        int k = (_privateKey.Modulus.BitLength + 7) / 8;
        if (sig.Length < k)
        {
            var padded = new byte[k];
            Buffer.BlockCopy(sig, 0, padded, k - sig.Length, sig.Length);
            return padded;
        }
        if (sig.Length > k)
        {
            // Теоретически не должно случиться (s < n), но обрежем ведущие нули
            var trimmed = new byte[k];
            Buffer.BlockCopy(sig, sig.Length - k, trimmed, 0, k);
            return trimmed;
        }
        return sig;
    }

    /// <summary>EMSA-PKCS1-v1_5 encoded message: 0x00 0x01 [PS=0xFF...] 0x00 [DigestInfo]</summary>
    private byte[] BuildPkcs1V15(byte[] hash, HashAlgorithmName hashAlgorithm)
    {
        var digestInfo = PrefixDigest(hash, hashAlgorithm);
        int k = (_privateKey.Modulus.BitLength + 7) / 8;
        if (digestInfo.Length > k - 11)
            throw new InvalidOperationException("Hash + DigestInfo слишком велик для ключа.");

        var em = new byte[k];
        em[0] = 0x00;
        em[1] = 0x01;
        int psLen = k - digestInfo.Length - 3;
        for (int i = 0; i < psLen; i++) em[2 + i] = 0xFF;
        em[2 + psLen] = 0x00;
        Buffer.BlockCopy(digestInfo, 0, em, 3 + psLen, digestInfo.Length);
        return em;
    }

    /// <summary>ASN.1 DigestInfo prefix + hash.</summary>
    private static byte[] PrefixDigest(byte[] hash, HashAlgorithmName algo)
    {
        byte[] prefix = algo.Name switch
        {
            "SHA256" => new byte[] { 0x30, 0x31, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x01, 0x05, 0x00, 0x04, 0x20 },
            "SHA384" => new byte[] { 0x30, 0x41, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x02, 0x05, 0x00, 0x04, 0x30 },
            "SHA512" => new byte[] { 0x30, 0x51, 0x30, 0x0d, 0x06, 0x09, 0x60, 0x86, 0x48, 0x01, 0x65, 0x03, 0x04, 0x02, 0x03, 0x05, 0x00, 0x04, 0x40 },
            "SHA1"   => new byte[] { 0x30, 0x21, 0x30, 0x09, 0x06, 0x05, 0x2b, 0x0e, 0x03, 0x02, 0x1a, 0x05, 0x00, 0x04, 0x14 },
            _ => throw new NotSupportedException($"Хэш {algo.Name} не поддерживается.")
        };
        var result = new byte[prefix.Length + hash.Length];
        Buffer.BlockCopy(prefix, 0, result, 0, prefix.Length);
        Buffer.BlockCopy(hash, 0, result, prefix.Length, hash.Length);
        return result;
    }

    private static IDigest CreateDigest(string name) => name switch
    {
        "SHA256" => new Sha256Digest(),
        "SHA384" => new Sha384Digest(),
        "SHA512" => new Sha512Digest(),
        "SHA1"   => new Sha1Digest(),
        _ => throw new NotSupportedException($"Хэш {name} не поддерживается.")
    };

    private static RSAParameters ToRsaParameters(RsaPrivateCrtKeyParameters key)
    {
        return new RSAParameters
        {
            Modulus  = key.Modulus.ToByteArrayUnsigned(),
            Exponent = key.PublicExponent.ToByteArrayUnsigned(),
            D        = key.Exponent.ToByteArrayUnsigned(),
            P        = key.P.ToByteArrayUnsigned(),
            Q        = key.Q.ToByteArrayUnsigned(),
            DP       = key.DP.ToByteArrayUnsigned(),
            DQ       = key.DQ.ToByteArrayUnsigned(),
            InverseQ = key.QInv.ToByteArrayUnsigned(),
        };
    }
}
