namespace Domain.Entities;

public enum AddressLabelKind
{
    Unknown = 0,
    Exchange = 1,
    ExchangeHotWallet = 2,
    ExchangeColdWallet = 3,
    DexRouter = 4,
    Bridge = 5,
    Protocol = 6,
    Treasury = 7,
    Ens = 8,
    Manual = 9
}
