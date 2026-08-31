namespace UFOps.Core;

public sealed record OperationId
{
    public Guid Value { get; }

    private OperationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Operation ID cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public static OperationId New() => new(Guid.CreateVersion7());

    public static OperationId FromGuid(Guid value) => new(value);

    public static OperationId Parse(string text)
    {
        if (!TryParse(text, out var value))
        {
            throw new FormatException("Invalid operation ID.");
        }

        return value!;
    }

    public static bool TryParse(string? text, out OperationId? value)
    {
        if (Guid.TryParse(text, out var parsed) && parsed != Guid.Empty)
        {
            value = new OperationId(parsed);
            return true;
        }

        value = null;
        return false;
    }

    public override string ToString() => Value.ToString("D");
}

public sealed record EngineId
{
    public string Value { get; }

    public EngineId(string value)
    {
        Value = IdentifierRules.Validate(value, nameof(value));
    }

    public override string ToString() => Value;
}

public sealed record CapabilityId
{
    public string Value { get; }

    public CapabilityId(string value)
    {
        Value = IdentifierRules.Validate(value, nameof(value));
    }

    public override string ToString() => Value;
}

public sealed record ErrorCode
{
    public string Value { get; }

    public ErrorCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length is < 3 or > 96)
        {
            throw new ArgumentException("Error code must contain 3-96 characters.", nameof(value));
        }

        var segments = value.Split('.');
        if (segments.Length < 2 || segments.Any(segment => segment.Length == 0))
        {
            throw new ArgumentException("Error code must be a dotted identifier with no empty segments.", nameof(value));
        }

        foreach (var segment in segments)
        {
            if (segment[0] is < 'A' or > 'Z')
            {
                throw new ArgumentException("Every error-code segment must start with an uppercase letter.", nameof(value));
            }

            foreach (var character in segment)
            {
                if (!(character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_'))
                {
                    throw new ArgumentException("Error code contains unsupported characters.", nameof(value));
                }
            }
        }

        Value = value;
    }

    public override string ToString() => Value;
}

public sealed record OperationPlanFingerprint
{
    public string Value { get; }

    public OperationPlanFingerprint(string value)
    {
        Value = IdentifierRules.ValidateSha256(value, nameof(value));
    }

    public override string ToString() => Value;
}

internal static class IdentifierRules
{
    internal static string Validate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length is < 3 or > 96 || value[0] is < 'a' or > 'z')
        {
            throw new ArgumentException("Identifier must start with a lowercase letter and contain 3-96 characters.", parameterName);
        }

        var previousWasSeparator = false;
        foreach (var character in value)
        {
            var separator = character is '.' or '-';
            if (!(character is >= 'a' and <= 'z' or >= '0' and <= '9') && !separator)
            {
                throw new ArgumentException("Identifier contains unsupported characters.", parameterName);
            }

            if (separator && previousWasSeparator)
            {
                throw new ArgumentException("Identifier cannot contain adjacent separators.", parameterName);
            }

            previousWasSeparator = separator;
        }

        if (previousWasSeparator)
        {
            throw new ArgumentException("Identifier cannot end with a separator.", parameterName);
        }

        return value;
    }

    internal static string ValidateSha256(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal characters.", parameterName);
        }

        return value.ToLowerInvariant();
    }
}
