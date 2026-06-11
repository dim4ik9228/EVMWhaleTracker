using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Domain.Common;

namespace Domain.ValueObjects;

/// <summary>
/// Represents an EVM address in canonical EIP-55 checksum form.
/// </summary>
public sealed class Address : IEquatable<Address>
{
    private const int ExpectedLength = 42;
    private const int HexLength = 40;
    private const int KeccakRateBytes = 136;

    private static readonly ulong[] RoundConstants =
    [
        0x0000000000000001UL,
        0x0000000000008082UL,
        0x800000000000808AUL,
        0x8000000080008000UL,
        0x000000000000808BUL,
        0x0000000080000001UL,
        0x8000000080008081UL,
        0x8000000000008009UL,
        0x000000000000008AUL,
        0x0000000000000088UL,
        0x0000000080008009UL,
        0x000000008000000AUL,
        0x000000008000808BUL,
        0x800000000000008BUL,
        0x8000000000008089UL,
        0x8000000000008003UL,
        0x8000000000008002UL,
        0x8000000000000080UL,
        0x000000000000800AUL,
        0x800000008000000AUL,
        0x8000000080008081UL,
        0x8000000000008080UL,
        0x0000000080000001UL,
        0x8000000080008008UL
    ];

    private static readonly int[] RotationOffsets =
    [
        0, 1, 62, 28, 27,
        36, 44, 6, 55, 20,
        3, 10, 43, 25, 39,
        41, 45, 15, 21, 8,
        18, 2, 61, 56, 14
    ];

    /// <summary>
    /// Gets the canonical 0x-prefixed checksum address.
    /// </summary>
    public string Value { get; }

    private Address(string value) => Value = value;

    /// <summary>
    /// Creates an address and requires the input checksum to be valid when mixed case is supplied.
    /// </summary>
    public static Result<Address> Create(string? value)
    {
        if (!TryParseAddress(value, requireChecksum: true, out var address, out var error))
        {
            return Result<Address>.Failure(error);
        }

        return Result<Address>.Success(new Address(address));
    }

    /// <summary>
    /// Creates an address from user input.
    /// </summary>
    public static Result<Address> From(string? value) => Create(value);

    /// <summary>
    /// Normalizes a valid EVM address to checksum form without requiring the input to already be checksummed.
    /// </summary>
    public static Result<Address> Normalize(string? value)
    {
        if (!TryParseAddress(value, requireChecksum: false, out var address, out var error))
        {
            return Result<Address>.Failure(error);
        }

        return Result<Address>.Success(new Address(address));
    }

    public override string ToString() => Value;

    public bool Equals(Address? other)
        => other is not null && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Address other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    private static string BuildChecksumAddress(string hexPart)
    {
        var lowercaseHex = hexPart.ToLowerInvariant();
        var hash = Keccak256(Encoding.ASCII.GetBytes(lowercaseHex));

        var result = new char[HexLength + 2];
        result[0] = '0';
        result[1] = 'x';

        for (var i = 0; i < HexLength; i++)
        {
            var character = lowercaseHex[i];
            var hashNibble = GetNibble(hash[i / 2], i % 2 == 0);
            result[i + 2] = character is >= 'a' and <= 'f' && hashNibble >= 8
                ? char.ToUpperInvariant(character)
                : character;
        }

        return new string(result);
    }

    private static bool IsHexadecimal(string value)
    {
        foreach (var character in value)
        {
            var isDigit = character is >= '0' and <= '9';
            var isLowerHex = character is >= 'a' and <= 'f';
            var isUpperHex = character is >= 'A' and <= 'F';

            if (!isDigit && !isLowerHex && !isUpperHex)
            {
                return false;
            }
        }

        return true;
    }

    private static int GetNibble(byte value, bool highNibble)
        => highNibble ? (value >> 4) & 0x0F : value & 0x0F;

    private static bool TryParseAddress(string? value, bool requireChecksum, out string address, out Error error)
    {
        address = string.Empty;
        error = Errors.Address.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.Length != ExpectedLength)
        {
            error = Errors.Address.Length;
            return false;
        }

        if (!candidate.StartsWith("0x", StringComparison.Ordinal))
        {
            error = Errors.Address.Prefix;
            return false;
        }

        var hexPart = candidate[2..];
        if (!IsHexadecimal(hexPart))
        {
            error = Errors.Address.Hex;
            return false;
        }

        var checksummedAddress = BuildChecksumAddress(hexPart);
        if (requireChecksum && !string.Equals(candidate, checksummedAddress, StringComparison.Ordinal))
        {
            error = Errors.Address.Checksum;
            return false;
        }

        address = checksummedAddress;
        return true;
    }

    private static byte[] Keccak256(ReadOnlySpan<byte> input)
    {
        var state = new ulong[25];
        var block = new byte[KeccakRateBytes];
        var offset = 0;

        while (offset + KeccakRateBytes <= input.Length)
        {
            input.Slice(offset, KeccakRateBytes).CopyTo(block);
            AbsorbBlock(state, block);
            KeccakPermutation(state);
            offset += KeccakRateBytes;
        }

        Array.Clear(block, 0, block.Length);
        input[offset..].CopyTo(block);
        block[input.Length - offset] ^= 0x01;
        block[KeccakRateBytes - 1] ^= 0x80;

        AbsorbBlock(state, block);
        KeccakPermutation(state);

        var output = new byte[32];
        var outputOffset = 0;
        for (var lane = 0; lane < KeccakRateBytes / sizeof(ulong) && outputOffset < output.Length; lane++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(outputOffset), state[lane]);
            outputOffset += sizeof(ulong);
        }

        return output;
    }

    private static void AbsorbBlock(ulong[] state, ReadOnlySpan<byte> block)
    {
        for (var lane = 0; lane < KeccakRateBytes / sizeof(ulong); lane++)
        {
            state[lane] ^= BinaryPrimitives.ReadUInt64LittleEndian(block.Slice(lane * sizeof(ulong), sizeof(ulong)));
        }
    }

    private static void KeccakPermutation(ulong[] state)
    {
        var b = new ulong[25];
        var c = new ulong[5];
        var d = new ulong[5];

        for (var round = 0; round < RoundConstants.Length; round++)
        {
            for (var x = 0; x < 5; x++)
            {
                c[x] = state[x] ^ state[x + 5] ^ state[x + 10] ^ state[x + 15] ^ state[x + 20];
            }

            for (var x = 0; x < 5; x++)
            {
                d[x] = BitOperations.RotateLeft(c[(x + 1) % 5], 1) ^ c[(x + 4) % 5];
            }

            for (var i = 0; i < 25; i++)
            {
                state[i] ^= d[i % 5];
            }

            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 5; y++)
                {
                    var index = x + (5 * y);
                    var rotated = BitOperations.RotateLeft(state[index], RotationOffsets[index]);
                    var newX = y;
                    var newY = (2 * x + 3 * y) % 5;
                    b[newX + (5 * newY)] = rotated;
                }
            }

            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 5; y++)
                {
                    var index = x + (5 * y);
                    state[index] = b[index] ^ ((~b[((x + 1) % 5) + (5 * y)]) & b[((x + 2) % 5) + (5 * y)]);
                }
            }

            state[0] ^= RoundConstants[round];
        }
    }
}
