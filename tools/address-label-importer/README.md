# Address NameTag Extractor

Generates a reviewed-candidate JSON seed from raw explorer account tag dumps.

Put supported raw files here:

```text
data/address-labels/raw/
  accounts.eth..json
  accounts.bsc.json
  accounts.base.json
  accounts.arbitrum.json
```

Raw files source: https://github.com/dawsbot/eth-labels

Run from the repository root:

```powershell
dotnet run tools/address-label-importer/ImportAddressNameTags.cs
```

The default output is ignored by git:

```text
data/address-labels/generated/address-name-tags.seed.candidate.json
```

Optional arguments:

```powershell
dotnet run tools/address-label-importer/ImportAddressNameTags.cs -- --raw-dir data/address-labels/raw --output data/address-labels/generated/custom.json
dotnet run tools/address-label-importer/ImportAddressNameTags.cs -- --input data/address-labels/raw/accounts.bsc.json
dotnet run tools/address-label-importer/ImportAddressNameTags.cs -- --kind Exchange --kind Bridge --review-status reviewed --output data/address-labels/address-labels.exchange-bridge.seed.json
```

Generated files use `schemaVersion: 1`. The default `reviewStatus` is `candidate`; use `--review-status reviewed` only after the output has been reviewed and is intended for normal database import.
