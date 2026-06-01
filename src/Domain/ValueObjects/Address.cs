using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Domain.Common;

namespace Domain.ValueObjects;

public sealed record Address
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

    public string Value { get; }

    private Address(string value) => Value = value;

    public static Result<Address> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<Address>.Failure(Error.Validation("Address.Empty", "An EVM address is required."));
        }

        var candidate = value.Trim();
        if (candidate.Length != ExpectedLength)
        {
            return Result<Address>.Failure(Error.Validation("Address.Length", "An EVM address must be 42 characters long, including the 0x prefix."));
        }

        if (!candidate.StartsWith("0x", StringComparison.Ordinal))
        {
            return Result<Address>.Failure(Error.Validation("Address.Prefix", "An EVM address must start with the 0x prefix."));
        }

        var hexPart = candidate[2..];
        if (!IsHexadecimal(hexPart))
        {
            return Result<Address>.Failure(Error.Validation("Address.Hex", "An EVM address can only contain hexadecimal characters after the 0x prefix."));
        }

        var checksummedAddress = BuildChecksumAddress(hexPart);
        if (!string.Equals(candidate, checksummedAddress, StringComparison.Ordinal))
        {
            return Result<Address>.Failure(Error.Validation("Address.Checksum", "The address must use the EIP-55 checksum format."));
        }

        return Result<Address>.Success(new Address(checksummedAddress));
    }

    public static Result<Address> From(string? value) => Create(value);

    public override string ToString() => Value;

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
