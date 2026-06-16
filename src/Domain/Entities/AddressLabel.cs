using Domain.Common;
using Domain.ValueObjects;

namespace Domain.Entities;

/// <summary>
/// Durable label for an EVM address, optionally scoped to a specific chain.
/// </summary>
public sealed class AddressLabel
{
    public const int MaxLabelLength = 64;
    public const int MaxSourceReferenceLength = 512;

    public Guid Id { get; private set; }

    public Address Address { get; private set; } = null!;

    public SupportedChain? Chain { get; private set; }

    public string Label { get; private set; } = string.Empty;

    public AddressLabelKind Kind { get; private set; }

    public AddressLabelSource Source { get; private set; }

    public string? SourceReference { get; private set; }

    public int Priority { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private AddressLabel()
    {
        // Required by EF Core.
    }

    private AddressLabel(
        Guid id,
        Address address,
        SupportedChain? chain,
        string label,
        AddressLabelKind kind,
        AddressLabelSource source,
        string? sourceReference,
        int priority,
        bool isEnabled,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Address = address;
        Chain = chain;
        Label = label;
        Kind = kind;
        Source = source;
        SourceReference = sourceReference;
        Priority = priority;
        IsEnabled = isEnabled;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public static Result<AddressLabel> Create(
        Address? address,
        SupportedChain? chain,
        string? label,
        AddressLabelKind kind,
        AddressLabelSource source,
        string? sourceReference = null,
        int priority = 100,
        bool isEnabled = true)
    {
        if (address is null)
        {
            return Result<AddressLabel>.Failure(Errors.AddressLabel.AddressRequired);
        }

        var labelResult = NormalizeLabel(label);
        if (labelResult.IsFailure)
        {
            return Result<AddressLabel>.Failure(labelResult.Error);
        }

        var sourceReferenceResult = NormalizeSourceReference(sourceReference);
        if (sourceReferenceResult.IsFailure)
        {
            return Result<AddressLabel>.Failure(sourceReferenceResult.Error);
        }

        return Result<AddressLabel>.Success(new AddressLabel(
            Guid.NewGuid(),
            address,
            chain,
            labelResult.Value,
            kind,
            source,
            sourceReferenceResult.Value,
            priority,
            isEnabled,
            DateTimeOffset.UtcNow));
    }

    public static Result<AddressLabel> Create(
        string? address,
        SupportedChain? chain,
        string? label,
        AddressLabelKind kind,
        AddressLabelSource source,
        string? sourceReference = null,
        int priority = 100,
        bool isEnabled = true)
    {
        var addressResult = Address.Normalize(address);
        if (addressResult.IsFailure)
        {
            return Result<AddressLabel>.Failure(addressResult.Error);
        }

        return Create(addressResult.Value, chain, label, kind, source, sourceReference, priority, isEnabled);
    }

    public Result Update(
        string? label,
        AddressLabelKind kind,
        AddressLabelSource source,
        string? sourceReference,
        int priority,
        bool isEnabled)
    {
        var labelResult = NormalizeLabel(label);
        if (labelResult.IsFailure)
        {
            return Result.Failure(labelResult.Error);
        }

        var sourceReferenceResult = NormalizeSourceReference(sourceReference);
        if (sourceReferenceResult.IsFailure)
        {
            return Result.Failure(sourceReferenceResult.Error);
        }

        Label = labelResult.Value;
        Kind = kind;
        Source = source;
        SourceReference = sourceReferenceResult.Value;
        Priority = priority;
        IsEnabled = isEnabled;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        return Result.Success();
    }

    private static Result<string> NormalizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return Result<string>.Failure(Errors.AddressLabel.LabelRequired);
        }

        var normalizedLabel = label.Trim();
        if (normalizedLabel.Length > MaxLabelLength)
        {
            return Result<string>.Failure(Errors.AddressLabel.LabelTooLong);
        }

        return Result<string>.Success(normalizedLabel);
    }

    private static Result<string?> NormalizeSourceReference(string? sourceReference)
    {
        if (string.IsNullOrWhiteSpace(sourceReference))
        {
            return Result<string?>.Success(null);
        }

        var normalizedSourceReference = sourceReference.Trim();
        if (normalizedSourceReference.Length > MaxSourceReferenceLength)
        {
            return Result<string?>.Failure(Errors.AddressLabel.SourceReferenceTooLong);
        }

        return Result<string?>.Success(normalizedSourceReference);
    }
}
