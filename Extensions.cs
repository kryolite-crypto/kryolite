using System.Collections;
using System.Globalization;
using System.Numerics;
using ExtendedNumerics;

namespace Marccacoin;
    
    internal static class Extensions
    {
        public readonly static BigInteger TARGET_MAX = new BigInteger(new byte[32] {255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255}, true, true);
        readonly static BigInteger TARGET_MIN = BigInteger.Parse("000000000FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", NumberStyles.HexNumber);

        public static BigInteger ToWork(this Difficulty difficulty)
        {
            return BigRational.Divide(Extensions.TARGET_MAX, difficulty.ToTarget()).WholePart;
        }

        public static BigInteger ToWork(this Difficulty difficulty, BigInteger target)
        {
            return BigRational.Divide(Extensions.TARGET_MAX, target).WholePart;
        }

        public static BigInteger ToTarget(this Difficulty difficulty)
        {
            var exponent = difficulty.b0;
            var bytes = (TARGET_MAX >> exponent).ToByteArray();

            // work with big endian order
            if (BitConverter.IsLittleEndian) {
                Array.Reverse(bytes);
            }

            var fractionBytes = new byte[] {difficulty.b1, difficulty.b2, difficulty.b3, 0};

            if (BitConverter.IsLittleEndian) {
                Array.Reverse(fractionBytes);
            }

            var fraction = BitConverter.ToUInt32(fractionBytes) / (double)uint.MaxValue;

            fractionBytes = BitConverter.GetBytes(fraction);

            if (BitConverter.IsLittleEndian) {
                Array.Reverse(fractionBytes);
            }

            var offset = 1;
            for (int i = 0; i < 4; i++) {
                bytes[offset + i] = (byte)(bytes[offset + i] - fractionBytes[i]);
            }

            return new BigInteger(bytes, true, true);
        }

        public static BigInteger ToBigInteger(this SHA256Hash sha256)
        {
            return new BigInteger(sha256.Buffer, true, true);
        }

        public static Difficulty ToDifficulty(this BigInteger target)
{
            var bytes = target.ToByteArray();
            Array.Resize(ref bytes, 4);

            if (BitConverter.IsLittleEndian) {
                Array.Reverse(bytes);
            }

            return new Difficulty {
                b0 = (byte)BigInteger.Log(target, 2),
                b1 = bytes[1],
                b2 = bytes[2],
                b3 = bytes[3]
            };
        }

        internal const string DefaultForegroundColor = "\x1B[39m\x1B[22m"; // reset to default foreground color
        internal const string DefaultBackgroundColor = "\x1B[49m"; // reset to the background color

        public static void WriteColoredMessage(this TextWriter textWriter, string message, ConsoleColor? background, ConsoleColor? foreground)
        {
            // Order: backgroundcolor, foregroundcolor, Message, reset foregroundcolor, reset backgroundcolor
            if (background.HasValue)
            {
                textWriter.Write(GetBackgroundColorEscapeCode(background.Value));
            }
            if (foreground.HasValue)
            {
                textWriter.Write(GetForegroundColorEscapeCode(foreground.Value));
            }
            textWriter.Write(message);
            if (foreground.HasValue)
            {
                textWriter.Write(DefaultForegroundColor); // reset to default foreground color
            }
            if (background.HasValue)
            {
                textWriter.Write(DefaultBackgroundColor); // reset to the background color
            }
        }

        internal static string GetForegroundColorEscapeCode(ConsoleColor color)
        {
            return color switch
            {
                ConsoleColor.Black => "\x1B[30m",
                ConsoleColor.DarkRed => "\x1B[31m",
                ConsoleColor.DarkGreen => "\x1B[32m",
                ConsoleColor.DarkYellow => "\x1B[33m",
                ConsoleColor.DarkBlue => "\x1B[34m",
                ConsoleColor.DarkMagenta => "\x1B[35m",
                ConsoleColor.DarkCyan => "\x1B[36m",
                ConsoleColor.Gray => "\x1B[37m",
                ConsoleColor.Red => "\x1B[1m\x1B[31m",
                ConsoleColor.Green => "\x1B[1m\x1B[32m",
                ConsoleColor.Yellow => "\x1B[1m\x1B[33m",
                ConsoleColor.Blue => "\x1B[1m\x1B[34m",
                ConsoleColor.Magenta => "\x1B[1m\x1B[35m",
                ConsoleColor.Cyan => "\x1B[1m\x1B[36m",
                ConsoleColor.White => "\x1B[1m\x1B[37m",
                _ => DefaultForegroundColor // default foreground color
            };
        }

        internal static string GetBackgroundColorEscapeCode(ConsoleColor color)
        {
            return color switch
            {
                ConsoleColor.Black => "\x1B[40m",
                ConsoleColor.DarkRed => "\x1B[41m",
                ConsoleColor.DarkGreen => "\x1B[42m",
                ConsoleColor.DarkYellow => "\x1B[43m",
                ConsoleColor.DarkBlue => "\x1B[44m",
                ConsoleColor.DarkMagenta => "\x1B[45m",
                ConsoleColor.DarkCyan => "\x1B[46m",
                ConsoleColor.Gray => "\x1B[47m",
                _ => DefaultBackgroundColor // Use default background color
            };
        }

        public static IDisposable EnterReadLockEx(this ReaderWriterLockSlim rwlock)
        {
            return new ReadLock(rwlock);
        }

        public static IDisposable EnterWriteLockEx(this ReaderWriterLockSlim rwlock)
        {
            return new WriteLock(rwlock);
        }

        public class ReadLock : IDisposable
        {
            private ReaderWriterLockSlim rwlock;

            public ReadLock(ReaderWriterLockSlim rwlock)
            {
                this.rwlock = rwlock ?? throw new ArgumentNullException(nameof(rwlock));
                rwlock.EnterReadLock();
            }

            public void Dispose()
            {
                rwlock.ExitReadLock();
            }
        }

        public class WriteLock : IDisposable
        {
            private ReaderWriterLockSlim rwlock;

            public WriteLock(ReaderWriterLockSlim rwlock)
            {
                this.rwlock = rwlock ?? throw new ArgumentNullException(nameof(rwlock));
                rwlock.EnterWriteLock();
            }

            public void Dispose()
            {
                rwlock.ExitWriteLock();
            }
        }
}