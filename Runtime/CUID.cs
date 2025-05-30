using System;
using System.Buffers.Binary;
using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using JD.CUIDs.SHA3;

// A lightweight (and faster) non-cryptographic version of CUID 2 with no dependencies tailored for Unity.
// Based on https://github.com/visus-io/cuid.net which is based on https://github.com/paralleldrive/cuid2
// Include a slim version of SHA3 from https://github.com/bcgit/bc-csharp
namespace JD.CUIDs
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Cuid2
    {
        private const int DefaultLength = 24;

        private readonly long _counter;
        private readonly byte[] _fingerprint;
        private readonly int _maxLength;
        private readonly char _prefix;
        private readonly byte[] _random;
        private readonly long _timestamp;

        public static string Get(int maxLength = DefaultLength) => new Cuid2(maxLength).ToString();
        
        public Cuid2(int maxLength = DefaultLength)
        {
            _counter = Counter.Instance.Value;
            _maxLength = maxLength;

            _fingerprint = Context.IdentityFingerprint;
            _prefix = Utils.GenerateCharacterPrefix();
            _random = Utils.GenerateRandom(maxLength);
            _timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                _counter,
                StructuralComparisons.StructuralEqualityComparer.GetHashCode(_fingerprint),
                _prefix,
                _random,
                _timestamp);
        }

        public override string ToString()
        {
            if (_counter == 0 ||
                _fingerprint == null ||
                _maxLength == 0 ||
                _prefix == char.MinValue ||
                _random == null ||
                _timestamp == 0)
            {
                return new string('0', DefaultLength);
            }

            Span<byte> buffer = stackalloc byte[16];

            BinaryPrimitives.WriteInt64LittleEndian(buffer[..8], _timestamp);
            BinaryPrimitives.WriteInt64LittleEndian(buffer[^8..], _counter);

            Sha3Digest digest = new(512);

            digest.BlockUpdate(buffer.ToArray(), 0, buffer.Length);
            digest.BlockUpdate(_fingerprint, 0, _fingerprint.Length);
            digest.BlockUpdate(_random, 0, _random.Length);

            var hash = new byte[digest.GetByteLength()];
            digest.DoFinal(hash, 0);

            return _prefix + Utils.Encode(hash)[..(_maxLength - 1)];
        }

        private static class Context
        {
            public static readonly byte[] IdentityFingerprint = Fingerprint.Generate();
        }

        private sealed class Counter
        {
            // ReSharper disable once InconsistentNaming
            private static readonly Lazy<Counter> _counter = new(() => new Counter());

            private long _value;

            private Counter()
            {
                _value = BinaryPrimitives.ReadInt64LittleEndian(Utils.GenerateRandom()) * 476782367;
            }

            public static Counter Instance => _counter.Value;

            public long Value => Interlocked.Increment(ref _value);
        }
    }

    // TODO: Need to test the different platforms to makes sure this works (personally my use-case is within editor but should works well on runtime)
    internal static class Fingerprint
    {
        public static byte[] Generate()
        {
            return GenerateIdentity();
        }

        private static byte[] GenerateIdentity()
        {
            var identity = Encoding.UTF8.GetBytes(RetrieveSystemName());
            Span<byte> buffer = stackalloc byte[identity.Length + 40];

            identity.CopyTo(buffer[..identity.Length]);

            BinaryPrimitives.WriteInt32LittleEndian(
                buffer.Slice(identity.Length + 1, 4),
                Process.GetCurrentProcess().Id
            );

            BinaryPrimitives.WriteInt32LittleEndian(
                buffer.Slice(identity.Length + 6, 4),
                Environment.CurrentManagedThreadId
            );

            Utils.GenerateRandom(32).CopyTo(buffer[^32..]);

            return buffer.ToArray();
        }

        private static string GenerateSystemName()
        {
            var bytes = Utils.GenerateRandom(32);
            var hostname = BitConverter.ToString(bytes);
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? hostname[..15]
                : hostname;
        }

        private static string RetrieveSystemName()
        {
            string machineName;
            try
            {
                machineName = !string.IsNullOrWhiteSpace(Environment.MachineName)
                    ? Environment.MachineName
                    : GenerateSystemName();
            }
            catch (InvalidOperationException)
            {
                machineName = GenerateSystemName();
            }

            return machineName;
        }
    }

    internal static class Utils
    {
        private const int Radix = 36;
        
        private static readonly BigInteger _bigRadix = new(36);
        private static readonly double _bitsPerDigit = Math.Log(36, 2);
        private static readonly Random _random = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long Decode(ReadOnlySpan<char> input)
        {
            long result = 0;
            var str = input.ToString();
            for (var i = 0; i < str.Length; i++)
            {
                var s = str[i];
                var c = (s >= '0' && s <= '9') ? s - '0' : 10 + s - 'a';
                result = (result * Radix) + c;
            }
            return result;
        }

        internal static string Encode(ReadOnlySpan<byte> value)
        {
            if (value.IsEmpty) return string.Empty;

            var length = (int) Math.Ceiling(value.Length * 8 / _bitsPerDigit);
            var i = length;
            Span<char> buffer = stackalloc char[length];

            var d = new BigInteger(value, true);
            while (!d.IsZero)
            {
                d = BigInteger.DivRem(d, _bigRadix, out var r);
                var c = (int)r;

                buffer[--i] = (char)(c is >= 0 and <= 9 ? c + 48 : c + 'a' - 10);
            }

            return new string(buffer.Slice(i, length - i));
        }

        internal static string Encode(ulong value)
        {
            if (value is 0) return string.Empty;

            const int length = 32;
            var i = length;
            Span<char> buffer = stackalloc char[length];

            do
            {
                var c = value % Radix;
                buffer[--i] = (char)(c <= 9 ? c + 48 : c + 'a' - 10);

                value /= Radix;
            } while (value > 0);

            return new string(buffer.Slice(i, length - i));
        }

        internal static char GenerateCharacterPrefix()
        {
            return (char) _random.Next(97, 122);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte[] GenerateRandom(int length = 8)
        {
            var seed = new byte[length];
            for (var i = 0; i < length; i++)
                seed[i] = (byte) _random.Next(256);
            return seed;
        }
    }
}