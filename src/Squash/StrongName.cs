namespace Squash;

/// <summary>
/// Signs an assembly again after the linker has rewritten it. The linker keeps the public key and
/// the signed flag and leaves the signature itself as zeros, which .NET never checks and
/// .NET Framework can refuse to load.
/// </summary>
public static class StrongName
{
    // Offsets into the public key blob an assembly carries: three header fields, then a
    // PUBLICKEYBLOB whose modulus starts 20 bytes in.
    const int publicKeyHashAlgorithmOffset = 4;
    const int publicKeyModulusOffset = 32;

    // Offsets into a PRIVATEKEYBLOB, which is what a .snk file is.
    const int keyBlobTypeOffset = 0;
    const int keyBlobMagicOffset = 8;
    const int keyBlobBitLengthOffset = 12;
    const int keyBlobExponentOffset = 16;
    const int keyBlobModulusOffset = 20;
    const byte privateKeyBlobType = 0x07;
    const int privateKeyMagic = 0x32415352;

    /// <summary>
    /// Whether the image carries a real signature. Public-signed and delay-signed images have the
    /// slot, and may have the flag, but the slot is all zeros.
    /// </summary>
    public static bool IsSigned(byte[] image)
    {
        var pe = PeFile.Read(image);
        if ((PeFile.Int32(image, pe.FlagsOffset) & PeFile.StrongNameSignedFlag) == 0)
        {
            return false;
        }

        for (var index = 0; index < pe.SignatureSize; index++)
        {
            if (image[pe.SignatureOffset + index] != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Signs the image in place with the key pair in a .snk blob.
    /// </summary>
    public static void Sign(byte[] image, byte[] publicKey, byte[] keyBlob)
    {
        var pe = PeFile.Read(image);
        if (pe.SignatureSize == 0 ||
            publicKey.Length <= publicKeyModulusOffset)
        {
            throw new SquashException(Diagnostics.CannotResign, "The assembly has no room for a strong-name signature.");
        }

        var parameters = ReadPrivateKey(keyBlob);
        if (!publicKey.Skip(publicKeyModulusOffset).SequenceEqual(parameters.Modulus!.Reverse()))
        {
            throw new SquashException(
                Diagnostics.CannotResign,
                "The key file is not the key the assembly was signed with. An assembly signed with a separate signature key (AssemblySignatureKeyAttribute) cannot be signed again.");
        }

        var algorithm = Algorithm(publicKey);
        var hash = Hash(image, pe, algorithm);

        // RSA.Create and ImportParameters, not RSACryptoServiceProvider: that needs a key container,
        // which a build agent running without a user profile does not have.
        using var rsa = RSA.Create();
        rsa.ImportParameters(parameters);
        var signature = rsa.SignHash(hash, algorithm, RSASignaturePadding.Pkcs1);
        if (signature.Length != pe.SignatureSize)
        {
            throw new SquashException(Diagnostics.CannotResign, "The signature does not fit the space the assembly reserves for it.");
        }

        // Stored least significant byte first.
        Array.Reverse(signature);
        Buffer.BlockCopy(signature, 0, image, pe.SignatureOffset, signature.Length);
        image[pe.FlagsOffset] |= PeFile.StrongNameSignedFlag;
    }

    /// <summary>
    /// Whether the image's signature is valid for the public key it carries.
    /// </summary>
    public static bool Verify(byte[] image, byte[] publicKey)
    {
        var pe = PeFile.Read(image);
        if (pe.SignatureSize == 0 ||
            publicKey.Length <= publicKeyModulusOffset)
        {
            return false;
        }

        var signature = new byte[pe.SignatureSize];
        Buffer.BlockCopy(image, pe.SignatureOffset, signature, 0, signature.Length);
        Array.Reverse(signature);

        var parameters = new RSAParameters
        {
            Modulus = publicKey.Skip(publicKeyModulusOffset).Reverse().ToArray(),
            Exponent = Exponent(publicKey, publicKeyModulusOffset - 4)
        };

        var algorithm = Algorithm(publicKey);
        using var rsa = RSA.Create();
        rsa.ImportParameters(parameters);
        return rsa.VerifyHash(Hash(image, pe, algorithm), signature, algorithm, RSASignaturePadding.Pkcs1);
    }

    /// <summary>
    /// The hash the CLR computes: the headers, then each section's data, leaving out the signature
    /// itself.
    /// </summary>
    static byte[] Hash(byte[] image, PeFile pe, HashAlgorithmName algorithm)
    {
        using var hash = IncrementalHash.CreateHash(algorithm);

        // Two header fields are hashed as zero. The checksum is computed after signing, and an
        // Authenticode signature is added after that; neither may invalidate the strong name.
        var headers = new byte[pe.HeadersEnd];
        Buffer.BlockCopy(image, 0, headers, 0, headers.Length);
        Array.Clear(headers, pe.CheckSumOffset, 4);
        Array.Clear(headers, pe.CertificateEntryOffset, 8);
        hash.AppendData(headers);

        var skipStart = pe.SignatureOffset;
        var skipEnd = pe.SignatureOffset + pe.SignatureSize;
        foreach (var section in pe.Sections)
        {
            var start = section.RawPointer;
            var end = Math.Min(start + section.RawSize, image.Length);
            if (skipEnd <= start ||
                skipStart >= end)
            {
                hash.AppendData(image, start, end - start);
                continue;
            }

            if (skipStart > start)
            {
                hash.AppendData(image, start, skipStart - start);
            }

            if (skipEnd < end)
            {
                hash.AppendData(image, skipEnd, end - skipEnd);
            }
        }

        return hash.GetHashAndReset();
    }

    static HashAlgorithmName Algorithm(byte[] publicKey) =>
        PeFile.Int32(publicKey, publicKeyHashAlgorithmOffset) switch
        {
            0x800C => HashAlgorithmName.SHA256,
            0x800D => HashAlgorithmName.SHA384,
            0x800E => HashAlgorithmName.SHA512,
            // 0x8004, and 0 which means the default.
            _ => HashAlgorithmName.SHA1
        };

    static RSAParameters ReadPrivateKey(byte[] blob)
    {
        if (blob.Length < keyBlobModulusOffset ||
            blob[keyBlobTypeOffset] != privateKeyBlobType ||
            PeFile.Int32(blob, keyBlobMagicOffset) != privateKeyMagic)
        {
            throw new SquashException(
                Diagnostics.CannotResign,
                "The key file does not hold a private key. A public key is enough to public-sign or delay-sign, but not to sign.");
        }

        // Every number is stored least significant byte first, and RSAParameters wants the opposite.
        var size = PeFile.Int32(blob, keyBlobBitLengthOffset) / 8;
        var half = size / 2;
        var offset = keyBlobModulusOffset;

        byte[] Next(int length)
        {
            var value = new byte[length];
            Buffer.BlockCopy(blob, offset, value, 0, length);
            Array.Reverse(value);
            offset += length;
            return value;
        }

        return new()
        {
            Exponent = Exponent(blob, keyBlobExponentOffset),
            Modulus = Next(size),
            P = Next(half),
            Q = Next(half),
            DP = Next(half),
            DQ = Next(half),
            InverseQ = Next(half),
            D = Next(size)
        };
    }

    /// <summary>
    /// Four bytes, least significant first, to the big-endian form without leading zeros.
    /// </summary>
    static byte[] Exponent(byte[] blob, int offset)
    {
        var bytes = new[]
        {
            blob[offset + 3],
            blob[offset + 2],
            blob[offset + 1],
            blob[offset]
        };
        return bytes.SkipWhile(_ => _ == 0).ToArray();
    }
}
