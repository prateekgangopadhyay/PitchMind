using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PitchMind.Contracts.Telemetry.V1;

/// <summary>Rejects JSON numbers that would be rounded when represented as a .NET decimal.</summary>
public sealed class ExactDecimalJsonConverter : JsonConverter<decimal>
{
    private static readonly BigInteger MaximumCoefficient = new(decimal.MaxValue);

    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
            throw new JsonException("A decimal telemetry value must be a JSON number.");

        var bytes = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan.ToArray();
        var raw = Encoding.UTF8.GetString(bytes);
        if (!IsExactlyRepresentable(raw))
            throw new JsonException("The JSON number is not exactly representable by .NET decimal.");
        if (IsZero(raw)) return 0m;
        if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new JsonException("The JSON number is not exactly representable by .NET decimal.");

        return value;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => writer.WriteNumberValue(value);

    private static bool IsExactlyRepresentable(string raw)
    {
        var exponentIndex = raw.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? raw : raw[..exponentIndex];
        var fractionDigits = 0;
        var decimalPoint = mantissa.IndexOf('.');
        if (decimalPoint >= 0) fractionDigits = mantissa.Length - decimalPoint - 1;
        var digits = decimalPoint < 0 ? mantissa : string.Concat(mantissa.AsSpan(0, decimalPoint), mantissa.AsSpan(decimalPoint + 1));
        if (digits.Length > 0 && digits[0] == '-') digits = digits[1..];
        if (!BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var coefficient))
            return false;
        if (coefficient.IsZero) return true;

        var exponentText = exponentIndex < 0 ? "0" : raw[(exponentIndex + 1)..];
        if (!int.TryParse(exponentText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exponent))
            return false;

        var scale = (long)fractionDigits - exponent;
        if (scale < 0)
        {
            if (-scale > 29) return false;
            coefficient *= BigInteger.Pow(10, (int)-scale);
            scale = 0;
        }
        while (scale > 0 && (scale > 28 || coefficient > MaximumCoefficient) && coefficient % 10 == 0)
        {
            coefficient /= 10;
            scale--;
        }

        return scale <= 28 && coefficient <= MaximumCoefficient;
    }

    private static bool IsZero(string raw)
    {
        var exponentIndex = raw.IndexOfAny(['e', 'E']);
        var mantissa = exponentIndex < 0 ? raw : raw[..exponentIndex];
        foreach (var character in mantissa)
            if (character is >= '1' and <= '9') return false;
        return true;
    }
}
