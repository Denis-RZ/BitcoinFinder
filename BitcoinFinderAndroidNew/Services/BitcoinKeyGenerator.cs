#nullable enable
using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using Nethereum.Signer;
using Org.BouncyCastle.Crypto.Digests;

namespace BitcoinFinderAndroidNew.Services
{
    public class BitcoinKeyGenerator
    {
        private static readonly byte[] MainnetPrivateKeyPrefix = { 0x80 };
        private static readonly byte[] TestnetPrivateKeyPrefix = { 0xEF };
        private static readonly byte[] CompressedPublicKeySuffix = { 0x01 };

        public static string GeneratePrivateKey(long index, KeyFormat format)
        {
            switch (format)
            {
                case KeyFormat.Decimal:
                    return index.ToString();
                case KeyFormat.Hex:
                    return $"0x{index:X}";
                default:
                    return index.ToString();
            }
        }

        public static string GenerateWIFKey(long index, bool compressed)
        {
            try
            {
                var privateKeyBytes = GetPrivateKeyBytes(index);
                var wifBytes = new byte[compressed ? 34 : 33];
                wifBytes[0] = 0x80; // Mainnet private key prefix
                Array.Copy(privateKeyBytes, 0, wifBytes, 1, 32);
                if (compressed)
                {
                    wifBytes[33] = 0x01;
                }
                return Base58Encode(wifBytes);
            }
            catch
            {
                return index.ToString();
            }
        }

        public static string GenerateBitcoinAddress(string privateKey, NetworkType network)
        {
            try
            {
                byte[] privateKeyBytes;
                if (privateKey.StartsWith("5") || privateKey.StartsWith("K") || privateKey.StartsWith("L"))
                {
                    privateKeyBytes = DecodeWIF(privateKey);
                }
                else if (privateKey.StartsWith("0x"))
                {
                    var hex = privateKey.Substring(2);
                    privateKeyBytes = Convert.FromHexString(hex.PadLeft(64, '0'));
                }
                else if (BigInteger.TryParse(privateKey, out var bigInt))
                {
                    privateKeyBytes = bigInt.ToByteArray(isUnsigned: true, isBigEndian: true);
                    if (privateKeyBytes.Length < 32)
                    {
                        var padded = new byte[32];
                        Array.Copy(privateKeyBytes, 0, padded, 32 - privateKeyBytes.Length, privateKeyBytes.Length);
                        privateKeyBytes = padded;
                    }
                }
                else
                {
                    return "";
                }

                // Генерируем публичный ключ через secp256k1
                var key = new EthECKey(privateKeyBytes, true);
                var pubKey = key.GetPubKey(true); // compressed
                var address = GetBitcoinAddressFromPubKey(pubKey, network);
                return address;
            }
            catch
            {
                return "";
            }
        }

        private static byte[] GetPrivateKeyBytes(long index)
        {
            var bytes = BitConverter.GetBytes(index);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            var padded = new byte[32];
            Array.Copy(bytes, 0, padded, 32 - bytes.Length, bytes.Length);
            return padded;
        }

        private static byte[] DecodeWIF(string wif)
        {
            var decoded = Base58Decode(wif);
            if (decoded.Length != 33 && decoded.Length != 34)
                throw new ArgumentException("Invalid WIF length");
            if (decoded[0] != 0x80)
                throw new ArgumentException("Invalid WIF prefix");
            var privateKey = new byte[32];
            Array.Copy(decoded, 1, privateKey, 0, 32);
            return privateKey;
        }

        private static string GetBitcoinAddressFromPubKey(byte[] pubKey, NetworkType network)
        {
            // 1. SHA256
            using var sha256 = SHA256.Create();
            var sha = sha256.ComputeHash(pubKey);
            // 2. RIPEMD160 (BouncyCastle)
            var ripe = new byte[20];
            var ripemd = new RipeMD160Digest();
            ripemd.BlockUpdate(sha, 0, sha.Length);
            ripemd.DoFinal(ripe, 0);
            // 3. Add version byte
            var addressBytes = new byte[21];
            addressBytes[0] = network == NetworkType.Mainnet ? (byte)0x00 : (byte)0x6F;
            Array.Copy(ripe, 0, addressBytes, 1, 20);
            // 4. Checksum
            var checksum = sha256.ComputeHash(sha256.ComputeHash(addressBytes)).Take(4).ToArray();
            var full = new byte[25];
            Array.Copy(addressBytes, 0, full, 0, 21);
            Array.Copy(checksum, 0, full, 21, 4);
            // 5. Base58
            return Base58Encode(full);
        }

        private static string Base58Encode(byte[] data)
        {
            const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
            var bi = new BigInteger(data.Reverse().ToArray());
            var s = "";
            while (bi > 0)
            {
                bi = BigInteger.DivRem(bi, 58, out var r);
                s = alphabet[(int)r] + s;
            }
            for (int i = 0; i < data.Length && data[i] == 0; i++)
            {
                s = "1" + s;
            }
            return s;
        }

        private static byte[] Base58Decode(string s)
        {
            const string alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
            var bi = BigInteger.Zero;
            for (int i = 0; i < s.Length; i++)
            {
                var c = s[i];
                var digit = alphabet.IndexOf(c);
                if (digit == -1)
                    throw new ArgumentException("Invalid Base58 character");
                bi = bi * 58 + digit;
            }
            var bytes = bi.ToByteArray();
            while (bytes.Length > 0 && bytes[0] == 0)
            {
                bytes = bytes.Skip(1).ToArray();
            }
            return bytes;
        }

        public static bool IsValidBitcoinAddress(string address)
        {
            if (string.IsNullOrEmpty(address))
                return false;
            if (address.Length < 26 || address.Length > 35)
                return false;
            if (!address.StartsWith("1") && !address.StartsWith("3") && !address.StartsWith("bc1"))
                return false;
            try
            {
                var decoded = Base58Decode(address);
                return decoded.Length == 25;
            }
            catch
            {
                return false;
            }
        }
    }
} 