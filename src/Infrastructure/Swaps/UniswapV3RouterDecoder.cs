using System.Numerics;
using Application.Swaps;

namespace Infrastructure.Swaps;

/// <summary>Pinned exact-input ABI families only; unknown multicall actions and split routes fall back.</summary>
internal static class UniswapV3RouterDecoder
{
    internal sealed record Path(string[] Tokens, int[] Fees, BigInteger AmountIn, string Recipient);
    internal static Path? Decode(TransactionExecutionEvidence evidence, string wallet)
    {
        try
        {
            var root = evidence.Calls.Single(x => x.Path.Length == 0);
            var actions = new List<Path>();
            void DecodeCall(string input, int depth)
            {
                if (depth > 4 || input.Length < 10 || input.Length > 65536) throw new FormatException();
                var selector = input[..10].ToLowerInvariant(); var data = input[10..];
                BigInteger Word(int index) => EthereumExecutionEvidenceProvider.HexString("0x" + data.Substring(index * 64, 64));
                string Address(int index)
                {
                    var word = data.Substring(index * 64, 64); if (word[..24].Any(x => x != '0')) throw new FormatException();
                    return "0x" + word[24..].ToLowerInvariant();
                }
                if (selector is "0xac9650d8" or "0x5ae401dc" or "0x1f0464d1")
                {
                    var offset = checked((int)Word(selector == "0xac9650d8" ? 0 : 1));
                    var count = checked((int)EthereumExecutionEvidenceProvider.HexString("0x" + data.Substring(offset * 2, 64)));
                    if (count is <= 0 or > 8) throw new FormatException();
                    for (var i = 0; i < count; i++)
                    {
                        var item = checked((int)EthereumExecutionEvidenceProvider.HexString("0x" + data.Substring((offset + 32 + i * 32) * 2, 64)));
                        var start = checked(offset + 32 + item);
                        var length = checked((int)EthereumExecutionEvidenceProvider.HexString("0x" + data.Substring(start * 2, 64)));
                        DecodeCall("0x" + data.Substring((start + 32) * 2, length * 2), depth + 1);
                    }
                    return;
                }
                if (selector is "0xb858183f" or "0xc04b8d59")
                {
                    var v1 = selector == "0xc04b8d59"; var tuple = checked((int)Word(0)) / 32;
                    var offset = checked((int)Word(tuple)); var start = checked(tuple * 32 + offset);
                    var length = checked((int)EthereumExecutionEvidenceProvider.HexString("0x" + data.Substring(start * 2, 64)));
                    if (length < 43 || length > 204 || (length - 20) % 23 != 0) throw new FormatException();
                    var path = data.Substring((start + 32) * 2, length * 2); var tokens = new List<string> { "0x" + path[..40].ToLowerInvariant() }; var fees = new List<int>();
                    for (var i = 40; i < path.Length; i += 46)
                    { fees.Add(Convert.ToInt32(path.Substring(i, 6), 16)); tokens.Add("0x" + path.Substring(i + 6, 40).ToLowerInvariant()); }
                    actions.Add(new(tokens.ToArray(), fees.ToArray(), Word(tuple + (v1 ? 3 : 2)), Address(tuple + 1))); return;
                }
                if (selector is "0x04e45aaf" or "0x414bf389")
                {
                    var v1 = selector == "0x414bf389";
                    if (Word(v1 ? 7 : 6) != 0) throw new FormatException();
                    actions.Add(new([Address(0), Address(1)], [checked((int)Word(2))], Word(v1 ? 5 : 4), Address(3))); return;
                }
                if (selector == "0x49404b7c") { if (Address(1) != wallet) throw new FormatException(); _ = Word(0); return; }
                if (selector == "0x49616997") { _ = Word(0); return; }
                if (selector == "0x12210e8a" && data.Length == 0) return;
                throw new FormatException();
            }
            DecodeCall(root.Input, 0);
            if (actions.Count != 1) return null;
            var result = actions[0];
            if (result.Recipient != wallet && result.Recipient != evidence.Destination && result.Recipient != "0x0000000000000000000000000000000000000001" && result.Recipient != "0x0000000000000000000000000000000000000002") return null;
            if (result.Fees.Length != evidence.Pools.Count) return null;
            for (var i = 0; i < result.Fees.Length; i++)
                if (!evidence.Pools.Any(p => p.Fee == result.Fees[i] && (p.Token0 == result.Tokens[i] && p.Token1 == result.Tokens[i + 1] || p.Token1 == result.Tokens[i] && p.Token0 == result.Tokens[i + 1]))) return null;
            return result;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentOutOfRangeException or OverflowException or InvalidOperationException) { return null; }
    }
}
