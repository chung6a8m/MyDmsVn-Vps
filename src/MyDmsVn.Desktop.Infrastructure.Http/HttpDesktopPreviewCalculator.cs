using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MyDmsVn.Desktop.Application;

namespace MyDmsVn.Desktop.Infrastructure.Http
{
    /// <summary>Provider-free fixed-point mirror of Server Domain revenue preview.</summary>
    public sealed class HttpDesktopPreviewCalculator : ISalesReceiptPreviewCalculator
    {
        private const decimal MaxQuantity = 99999999999999.9999m;
        private const decimal MaxPrice = 999999999999999.9999m;
        private const decimal MaxAmount = 999999999999999999999999.9999m;

        public decimal CalculateRevenue(decimal qty, decimal unitPrice, bool isGift)
        {
            var quantity = Scale4(qty, MaxQuantity, true);
            var price = Scale4(unitPrice, MaxPrice, false);
            if (isGift) return 0m;
            return Amount(Round(quantity * price, 10000));
        }

        public decimal SumAmounts(IEnumerable<decimal> amounts)
        {
            if (amounts == null) throw new ArgumentNullException(nameof(amounts));
            return Amount(amounts.Aggregate(BigInteger.Zero,
                (total, value) => total + Scale4(value, MaxAmount, false)));
        }

        private static BigInteger Scale4(decimal value, decimal max, bool positive)
        {
            if (value < 0 || positive && value == 0 || value > max)
                throw new ArgumentOutOfRangeException(nameof(value));
            var bits = decimal.GetBits(value);
            var scale = (bits[3] >> 16) & 255;
            var coefficient = (BigInteger)(uint)bits[0] +
                ((BigInteger)(uint)bits[1] << 32) +
                ((BigInteger)(uint)bits[2] << 64);
            if (scale <= 4) return coefficient * BigInteger.Pow(10, 4 - scale);
            var divisor = BigInteger.Pow(10, scale - 4);
            if (coefficient % divisor != 0)
                throw new ArgumentException("At most four fractional places.", nameof(value));
            return coefficient / divisor;
        }

        private static BigInteger Round(BigInteger numerator, BigInteger denominator)
        {
            var value = BigInteger.DivRem(numerator, denominator, out var remainder);
            return value + (remainder * 2 >= denominator ? BigInteger.One : BigInteger.Zero);
        }

        private static decimal Amount(BigInteger value)
        {
            if (value < 0 || value > new BigInteger(MaxAmount * 10000m))
                throw new OverflowException("Amount exceeds storage precision.");
            return new decimal((int)(uint)(value & uint.MaxValue),
                (int)(uint)((value >> 32) & uint.MaxValue),
                (int)(uint)((value >> 64) & uint.MaxValue), false, 4);
        }
    }
}
